using FaizaCash.Domain;
using FaizaCash.Application;
using FaizaCash.Infrastructure;
using FaizaCash.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static FaizaCash.Api.RoleNames;
using static FaizaCash.Api.EndpointHelpers;
using static FaizaCash.Api.OperationsValidators;
using static FaizaCash.Api.BackupResults;

namespace FaizaCash.Api;

/// <summary>Модуль «Analytics»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class AnalyticsEndpoints
{
    private sealed record AttentionItem(string Code,string Severity,string Title,string Message,decimal? Amount,string Href,long? ObjectId);
    private sealed record NotificationItem(string Key,string Priority,DateOnly Date,string Reason,string ObjectType,long? ObjectId,string ActionHref);
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var summary = app.MapGroup("/api/summary").RequireAuthorization().WithTags("Analytics");
        var notifications = app.MapGroup("/api/notifications").RequireAuthorization().WithTags("Analytics");
        var dashboard = app.MapGroup("/api/dashboard").RequireAuthorization().WithTags("Analytics");
        var categorySummary = app.MapGroup("/api/category-summary").RequireAuthorization().WithTags("Analytics");
        var analytics = app.MapGroup("/api/analytics").RequireAuthorization().WithTags("Analytics");
summary.MapGet("", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,IBalanceService balances,long cashboxId, DateOnly? from, DateOnly? to) =>
{
    await access.Require(ctx.UserId(),cashboxId,ctx.RequestAborted);
    var end = to ?? Local.Today; var start = from ?? (end.DayNumber>=30?end.AddDays(-30):DateOnly.MinValue);
    if(start==DateOnly.MinValue||start>end||end.DayNumber-start.DayNumber>3660)throw new BusinessException("Укажите корректный период отчёта.",400);
    await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ctx.RequestAborted);
    var period=await balances.PeriodOpening([cashboxId],start,end,ctx.RequestAborted);
    var totals=await db.Operations.AsNoTracking().Where(x=>x.CashboxId==cashboxId&&x.Status=="posted"&&x.Date>=start&&x.Date<=end)
        .GroupBy(_=>1).Select(group=>new
        {
            Income=group.Where(x=>x.Type=="income").Sum(x=>x.Amount),
            Expense=group.Where(x=>x.Type=="expense").Sum(x=>x.Amount)
        }).SingleOrDefaultAsync(ctx.RequestAborted);
    var income=totals?.Income??0m;var expense=totals?.Expense??0m;
    await tx.CommitAsync(ctx.RequestAborted);
    return Results.Ok(new{from=start,to=end,opening=period.Opening,activation=period.ActivationTotal,income,expense,closing=period.Opening+period.ActivationTotal+income-expense});
});
notifications.MapGet("", [Authorize] async (HttpContext ctx, CashDb db, ICashboxAccess access, BackupService backups, IBalanceService balanceService) =>
{
    var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
    var today=Local.Today;
    var items=new List<NotificationItem>();
    var canViewDebts=ctx.User.IsInRole(Admin)||ctx.User.IsInRole(Accountant)||ctx.User.IsInRole(Manager)||ctx.User.IsInRole(Auditor);
    if(canViewDebts)
    {
        var overdue=await db.Settlements.AsNoTracking().CountAsync(x=>(x.Status=="open"||x.Status=="partially_paid")&&x.DueDate<today&&(ctx.User.IsInRole(Admin)||(x.CashboxId.HasValue&&allowed.Contains(x.CashboxId.Value))||x.Allocations.Any(a=>allowed.Contains(a.Operation.CashboxId))),ctx.RequestAborted);
        if(overdue>0) items.Add(new("overdue_debt","warning",today,$"Просроченных задолженностей: {overdue}.","settlements",null,"#/debts"));
    }
    var boxes=await db.Cashboxes.AsNoTracking().Where(x=>allowed.Contains(x.Id)&&x.Active&&x.BalanceLimit!=null).ToListAsync(ctx.RequestAborted);
    var limitBalances=await balanceService.BalancesAt(boxes.Select(x=>x.Id).ToArray(),today,ctx.RequestAborted);
    foreach(var box in boxes)
    {
        var balance=limitBalances.GetValueOrDefault(box.Id);
        if(balance>box.BalanceLimit) items.Add(new($"cash_limit:{box.Id}","warning",today,$"Касса {box.Code} превысила лимит остатка.","cashbox",box.Id,$"#/operations?box={box.Id}"));
    }
    var gaps=await db.Shifts.AsNoTracking().CountAsync(x=>allowed.Contains(x.CashboxId)&&x.Status=="closed"&&x.ClosingDifference!=null&&x.ClosingDifference!=0&&x.ClosedAt>=DateTime.UtcNow.AddDays(-14),ctx.RequestAborted);
    if(gaps>0) items.Add(new("shift_discrepancy","warning",today,$"Смен с расхождением за 14 дней: {gaps}.","shifts",null,"#/shifts"));
    if(ctx.User.IsInRole(Admin))
    {
        var status=await backups.StatusAsync(ctx.RequestAborted);
        if(status.Status=="failed") items.Add(new("backup_failed","danger",today,"Последняя попытка резервного копирования завершилась ошибкой.","backup",null,"#/backups"));
    }
    var keys=items.Select(x=>x.Key).ToArray();
    var read=await db.NotificationReads.AsNoTracking().Where(x=>x.UserId==ctx.UserId()&&keys.Contains(x.NotificationKey)).Select(x=>x.NotificationKey).ToListAsync(ctx.RequestAborted);
    return Results.Ok(items.Select(x=>new {key=x.Key,priority=x.Priority,date=x.Date,reason=x.Reason,objectType=x.ObjectType,objectId=x.ObjectId,actionHref=x.ActionHref,isRead=read.Contains(x.Key)}));
});
notifications.MapPost("/{key}/read", [Authorize] async (string key, HttpContext ctx, CashDb db, Audit audit) =>
{
    if(key.Length is < 1 or > 200 || !System.Text.RegularExpressions.Regex.IsMatch(key,"^[a-z0-9_:-]+$")) return Results.BadRequest();
    var allowed=await ctx.RequestServices.GetRequiredService<ICashboxAccess>().AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
    var visible=false;
    if(key=="overdue_debt" && (ctx.User.IsInRole(Admin)||ctx.User.IsInRole(Accountant)||ctx.User.IsInRole(Manager)||ctx.User.IsInRole(Auditor)))
        visible=await db.Settlements.AsNoTracking().AnyAsync(x=>(x.Status=="open"||x.Status=="partially_paid")&&x.DueDate<Local.Today&&(ctx.User.IsInRole(Admin)||(x.CashboxId.HasValue&&allowed.Contains(x.CashboxId.Value))||x.Allocations.Any(a=>allowed.Contains(a.Operation.CashboxId))),ctx.RequestAborted);
    else if(key=="shift_discrepancy")
        visible=await db.Shifts.AsNoTracking().AnyAsync(x=>allowed.Contains(x.CashboxId)&&x.Status=="closed"&&x.ClosingDifference!=null&&x.ClosingDifference!=0&&x.ClosedAt>=DateTime.UtcNow.AddDays(-14),ctx.RequestAborted);
    else if(key=="backup_failed" && ctx.User.IsInRole(Admin))
        visible=(await ctx.RequestServices.GetRequiredService<BackupService>().StatusAsync(ctx.RequestAborted)).Status=="failed";
    else if(key.StartsWith("cash_limit:",StringComparison.Ordinal) && long.TryParse(key.AsSpan("cash_limit:".Length),out var cashboxId) && allowed.Contains(cashboxId))
    {
        var cashbox=await db.Cashboxes.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==cashboxId&&x.Active&&x.BalanceLimit!=null,ctx.RequestAborted);
        visible=cashbox is not null && await ctx.RequestServices.GetRequiredService<IBalanceService>().BalanceAt(cashboxId,Local.Today,ctx.RequestAborted)>cashbox.BalanceLimit;
    }
    if(!visible)return Results.NotFound();
    var inserted=await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"NotificationReads\" (\"UserId\",\"NotificationKey\",\"ReadAt\") VALUES ({ctx.UserId()},{key},{DateTime.UtcNow}) ON CONFLICT (\"UserId\",\"NotificationKey\") DO NOTHING",ctx.RequestAborted);
    if(inserted>0)await audit.Add(ctx.UserId(),"notification_read","notification",null,new {key});
    return Results.NoContent();
});
dashboard.MapGet("", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,IBalanceService balanceService,DateOnly? from,DateOnly? to,long? branchId,long? cashboxId,long? categoryId,long? counterpartyId) =>
{
    var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted); var end=to??Local.Today; var start=from??end.AddDays(-30);
    if(start>end||end.DayNumber-start.DayNumber>3660)return Results.BadRequest();
    var canViewDebts=ctx.User.IsInRole(Admin)||ctx.User.IsInRole(Accountant)||ctx.User.IsInRole(Manager)||ctx.User.IsInRole(Auditor);
    var boxes=await db.Cashboxes.AsNoTracking().Include(x=>x.Branch).Where(x=>allowed.Contains(x.Id)&&(!branchId.HasValue||x.BranchId==branchId)&&(!cashboxId.HasValue||x.Id==cashboxId)).ToListAsync(ctx.RequestAborted);
    var boxIds=boxes.Select(x=>x.Id).ToArray();
    // FG7-001: one balance source; a cashbox activated after the period end is absent, not a zero row.
    var existing=(await balanceService.ExistingOn(boxIds,end,ctx.RequestAborted)).ToHashSet();
    var endBalances=await balanceService.BalancesAt(boxIds,end,ctx.RequestAborted);
    var balances=boxes.Where(b=>existing.Contains(b.Id)).Select(b=>new{b.Id,b.Code,b.Name,branchId=b.BranchId,branch=b.Branch.Name,currency=b.CurrencyId,limit=b.BalanceLimit,balance=endBalances.GetValueOrDefault(b.Id)}).ToArray();
    var external=db.Operations.AsNoTracking().Where(x=>boxIds.Contains(x.CashboxId)&&x.Status=="posted"&&x.TransferId==null&&x.Date>=start&&x.Date<=end);
    if(categoryId.HasValue) external=external.Where(x=>x.CategoryId==categoryId);
    if(counterpartyId.HasValue) external=external.Where(x=>x.CounterpartyId==counterpartyId);
    var totals=await external.GroupBy(_=>1).Select(g=>new{income=g.Where(x=>x.Type=="income").Sum(x=>x.Amount),expense=g.Where(x=>x.Type=="expense").Sum(x=>x.Amount),count=g.Count()}).SingleOrDefaultAsync(ctx.RequestAborted);
    var income=totals?.income??0m; var expense=totals?.expense??0m;
    var transfers=await db.Transfers.AsNoTracking().Where(x=>boxIds.Contains(x.SourceCashboxId)&&x.Date>=start&&x.Date<=end).SumAsync(x=>(decimal?)x.Amount,ctx.RequestAborted)??0m;
    var today=Local.Today;
    var monthStart=new DateOnly(today.Year,today.Month,1);
    var windowStart=today.AddDays(-6)<monthStart?today.AddDays(-6):monthStart;
    var windowOperations=db.Operations.AsNoTracking().Where(x=>boxIds.Contains(x.CashboxId)&&x.Status=="posted"&&x.TransferId==null&&x.Date>=windowStart&&x.Date<=today);
    if(categoryId.HasValue)windowOperations=windowOperations.Where(x=>x.CategoryId==categoryId);
    if(counterpartyId.HasValue)windowOperations=windowOperations.Where(x=>x.CounterpartyId==counterpartyId);
    var windowTotals=await windowOperations.GroupBy(_=>1).Select(g=>new
    {
        todayIncome=g.Where(x=>x.Date==today&&x.Type=="income").Sum(x=>x.Amount),
        todayExpense=g.Where(x=>x.Date==today&&x.Type=="expense").Sum(x=>x.Amount),
        weekIncome=g.Where(x=>x.Date>=today.AddDays(-6)&&x.Type=="income").Sum(x=>x.Amount),
        weekExpense=g.Where(x=>x.Date>=today.AddDays(-6)&&x.Type=="expense").Sum(x=>x.Amount),
        monthIncome=g.Where(x=>x.Date>=monthStart&&x.Type=="income").Sum(x=>x.Amount),
        monthExpense=g.Where(x=>x.Date>=monthStart&&x.Type=="expense").Sum(x=>x.Amount)
    }).SingleOrDefaultAsync(ctx.RequestAborted);
    var periodTotals=new
    {
        todayIncome=windowTotals?.todayIncome??0m,todayExpense=windowTotals?.todayExpense??0m,
        weekIncome=windowTotals?.weekIncome??0m,weekExpense=windowTotals?.weekExpense??0m,
        monthIncome=windowTotals?.monthIncome??0m,monthExpense=windowTotals?.monthExpense??0m
    };
    var scopedSettlements=db.Settlements.AsNoTracking().Where(x=>x.Status!="cancelled"&&(ctx.User.IsInRole(Admin)||(x.CashboxId.HasValue&&boxIds.Contains(x.CashboxId.Value))||x.Allocations.Any(a=>boxIds.Contains(a.Operation.CashboxId))));
    var debt=await scopedSettlements.Where(x=>canViewDebts&&x.Status!="paid").GroupBy(x=>x.Direction).Select(g=>new{direction=g.Key,total=g.Sum(x=>x.TotalAmount-x.PaidAmount)}).ToListAsync(ctx.RequestAborted);
    var overdue=canViewDebts?await scopedSettlements.Where(x=>x.Status!="paid"&&x.DueDate<today).SumAsync(x=>(decimal?)(x.TotalAmount-x.PaidAmount),ctx.RequestAborted)??0m:0m;
    var debtStatuses=await scopedSettlements.Where(x=>canViewDebts).GroupBy(x=>x.Status).Select(g=>new{status=g.Key,count=g.Count(),total=g.Sum(x=>x.TotalAmount-x.PaidAmount)}).ToListAsync(ctx.RequestAborted);
    var categories=await external.GroupBy(x=>new{x.Category.Name,x.Type}).Select(g=>new{name=g.Key.Name,type=g.Key.Type,amount=g.Sum(x=>x.Amount)}).OrderByDescending(x=>x.amount).ToListAsync(ctx.RequestAborted);
    var latest=await db.Operations.AsNoTracking().Where(x=>boxIds.Contains(x.CashboxId)).OrderByDescending(x=>x.CreatedAt).Take(10).Select(x=>new{x.Id,x.CashboxId,x.Date,x.Type,x.Status,x.Amount,x.Description,x.DocumentNumber,transfer=x.TransferId!=null}).ToListAsync(ctx.RequestAborted);
    var openShifts=await db.Shifts.AsNoTracking().Where(x=>boxIds.Contains(x.CashboxId)&&x.Status=="open").Select(x=>new{x.Id,x.CashboxId,x.Date,x.OpeningBalance,x.Status}).ToListAsync(ctx.RequestAborted);
    var flow=await external.GroupBy(x=>x.Date).Select(g=>new
    {
        date=g.Key,
        income=g.Where(x=>x.Type=="income").Sum(x=>x.Amount),
        expense=g.Where(x=>x.Type=="expense").Sum(x=>x.Amount),
        net=g.Sum(x=>x.Type=="income"?x.Amount:-x.Amount)
    }).OrderBy(x=>x.date).ToListAsync(ctx.RequestAborted);
    var balanceTrend=(await balanceService.DailySeries(boxIds,start,end,ctx.RequestAborted)).Select(x=>new{date=x.Date,balance=x.Balance}).ToArray();
    var branches=balances.GroupBy(x=>new{x.branchId,x.branch}).Select(g=>new{id=g.Key.branchId,name=g.Key.branch,balance=g.Sum(x=>x.balance)}).ToList();
    var warnings=new List<object>();
    warnings.AddRange(openShifts.Where(x=>x.OpeningBalance<0).Select(x=>new{code="negative_shift_opening",message=$"Отрицательный начальный остаток смены #{x.Id}."}));
    warnings.AddRange(balances.Where(x=>x.limit.HasValue&&x.balance>x.limit.Value).Select(x=>new{code="cash_limit",cashboxId=x.Id,message=$"{x.branch} · {x.Name}: превышение лимита",amount=x.balance-x.limit!.Value}));
    if(overdue>0) warnings.Add(new{code="overdue_debt",message="Просроченная задолженность",amount=overdue});
    // Dashboard 2.0 "Требует внимания": built only from data that already exists in the schema.
    var attention=new List<AttentionItem>();
    var names=boxes.ToDictionary(x=>x.Id,x=>x.Name);
    var discrepancies=await db.Shifts.AsNoTracking().Where(x=>boxIds.Contains(x.CashboxId)&&x.Status=="closed"&&x.ClosingDifference!=null&&x.ClosingDifference!=0&&x.Date>=start&&x.Date<=end).OrderByDescending(x=>x.Date).Take(20).ToListAsync(ctx.RequestAborted);
    attention.AddRange(discrepancies.Select(x=>new AttentionItem("shift_discrepancy","warning",$"Расхождение при закрытии смены #{x.Id}",$"{names.GetValueOrDefault(x.CashboxId)} · {x.Date:dd.MM.yyyy}: факт отличается от расчётного остатка.",x.ClosingDifference,"#/shifts",x.Id)));
    attention.AddRange(balances.Where(x=>x.limit.HasValue&&x.balance>x.limit.Value).Select(x=>new AttentionItem("cash_limit","warning",$"Превышен лимит кассы {x.Name}",$"{x.branch}: остаток выше установленного лимита.",x.balance-x.limit!.Value,$"#/operations?box={x.Id}",x.Id)));
    if(canViewDebts&&overdue>0)attention.Add(new AttentionItem("overdue_debt","warning","Просроченная задолженность","Есть задолженности с истёкшим сроком оплаты.",overdue,"#/debts",null));
    if(ctx.User.IsInRole(Admin)&&(await ctx.RequestServices.GetRequiredService<BackupService>().StatusAsync(ctx.RequestAborted)).Status=="failed")
        attention.Add(new AttentionItem("backup_failed","danger","Резервное копирование не удалось","Последняя попытка создания копии завершилась ошибкой.",null,"#/backups",null));
    return Results.Ok(new{period=new{from=start,to=end},periodTotals,totalBalance=balances.Sum(x=>x.balance),income,expense,net=income-expense,operationCount=totals?.count??0,activeCashboxes=boxes.Count(x=>x.Active),transfers,overdue,canViewDebts,balances,branches,categories,debts=debt,debtStatuses,cashflow=flow,balanceTrend,latestOperations=latest,openShifts,warnings,attention});
});
categorySummary.MapGet("",[Authorize] async(HttpContext ctx,CashDb db,ICashboxAccess access,long cashboxId,DateOnly from,DateOnly to)=>{await access.Require(ctx.UserId(),cashboxId,ctx.RequestAborted);return Results.Ok(await db.Operations.Where(x=>x.CashboxId==cashboxId&&x.Status=="posted"&&x.Date>=from&&x.Date<=to).GroupBy(x=>new {x.Category.Name,x.Type}).Select(g=>new {g.Key.Name,g.Key.Type,amount=g.Sum(x=>x.Amount)}).OrderByDescending(x=>x.amount).ToListAsync());});
analytics.MapGet("",[Authorize] async(HttpContext ctx,CashDb db,ICashboxAccess access,long cashboxId,DateOnly from,DateOnly to)=>{await access.Require(ctx.UserId(),cashboxId,ctx.RequestAborted);return Results.Ok(await db.Operations.Where(x=>x.CashboxId==cashboxId && x.Status=="posted" && x.Date>=from && x.Date<=to).GroupBy(x=>new {x.Date,x.Type}).Select(g=>new {g.Key.Date,g.Key.Type,amount=g.Sum(x=>x.Amount)}).OrderBy(x=>x.Date).ToListAsync());});
        return app;
    }
}
