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

/// <summary>Модуль «Settlements»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class SettlementsEndpoints
{
    public static IEndpointRouteBuilder MapSettlementsEndpoints(this IEndpointRouteBuilder app)
    {
        var settlements = app.MapGroup("/api/settlements").RequireAuthorization().WithTags("Settlements");
settlements.MapGet("", [Authorize(Roles = Admin + "," + Accountant + "," + Manager + "," + Auditor)] async (CashDb db, HttpContext ctx, ICashboxAccess access, long? counterpartyId, Guid? representativeId, string? direction, string? status, string? q, bool? overdueOnly, DateOnly? asOf, int? page, int? pageSize, CancellationToken ct) =>
{
    var allowed=await access.AllowedCashboxes(ctx.UserId(),ct);
    var date=asOf??Local.Today;
    if(direction is not (null or "receivable" or "payable")||status is not (null or "open" or "partially_paid" or "paid" or "cancelled")||q?.Length>160||q?.Contains('\0')==true||date==DateOnly.MinValue)
        return Results.BadRequest();
    var query=db.Settlements.AsNoTracking()
        .Where(x=>(ctx.User.IsInRole(Admin)||(x.CashboxId.HasValue&&allowed.Contains(x.CashboxId.Value))||x.Allocations.Any(a=>allowed.Contains(a.Operation.CashboxId)))&&(!counterpartyId.HasValue||x.CounterpartyId==counterpartyId)&&(!representativeId.HasValue||x.RepresentativeId==representativeId)&&(direction==null||x.Direction==direction)&&(status==null||x.Status==status)&&(!overdueOnly.GetValueOrDefault()||(x.Status=="open"||x.Status=="partially_paid")&&x.DueDate<date)&&(q==null||(x.CounterpartyNameSnapshot!=null&&x.CounterpartyNameSnapshot.Contains(q))||x.Counterparty.Name.Contains(q)||x.BasisReference.Contains(q)))
        .OrderBy(x=>x.DueDate).ThenByDescending(x=>x.CreatedAt);
    return await ListPage(ctx,query.Select(x=>new{x.Id,x.CounterpartyId,cashboxId=x.CashboxId,cashbox=x.Cashbox==null?null:x.Cashbox.Name,counterparty=x.CounterpartyNameSnapshot??x.Counterparty.Name,representativeId=x.RepresentativeId,representative=db.Users.Where(u=>u.Id==x.RepresentativeId).Select(u=>u.FullName).FirstOrDefault(),x.Direction,x.BasisType,x.BasisReference,x.TotalAmount,x.PaidAmount,remaining=x.Status=="cancelled"?0m:x.TotalAmount-x.PaidAmount,x.Status,x.DueDate,overdue=(x.Status=="open"||x.Status=="partially_paid")&&x.DueDate<date}),page,pageSize,ct);
});
settlements.MapPost("", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (SettlementInput x, CashDb db, Audit audit, HttpContext ctx, ICashboxAccess access) =>
{
    if(x.CashboxId<=0)return Results.BadRequest();
    await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted);
    if(!await db.Cashboxes.AnyAsync(c=>c.Id==x.CashboxId&&c.Active,ctx.RequestAborted))return Results.BadRequest();
    var direction=x.Direction?.Trim().ToLowerInvariant(); var basis=x.BasisType?.Trim().ToLowerInvariant(); var reference=x.BasisReference?.Trim();
    if(!await db.Counterparties.AnyAsync(c=>c.Id==x.CounterpartyId&&c.Active,ctx.RequestAborted)||direction is not ("receivable" or "payable")||basis is not ("order" or "delivery" or "return" or "manual")||string.IsNullOrWhiteSpace(reference)||reference.Length>120||reference.Contains('\0')||!Money.IsValid(x.TotalAmount)||x.DueDate==DateOnly.MinValue||x.DueDate<DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)))return Results.BadRequest();
    if(x.RepresentativeId.HasValue&&!await db.Users.AnyAsync(u=>u.Id==x.RepresentativeId&&u.Active,ctx.RequestAborted))return Results.Problem("Ответственный представитель не найден или отключён.",statusCode:400);
    var party=await db.Counterparties.AsNoTracking().SingleAsync(c=>c.Id==x.CounterpartyId,ctx.RequestAborted);
    var settlement=new Settlement{CashboxId=x.CashboxId,CounterpartyId=x.CounterpartyId,CounterpartyNameSnapshot=party.Name,RepresentativeId=x.RepresentativeId,Direction=direction,BasisType=basis,BasisReference=reference,TotalAmount=x.TotalAmount,DueDate=x.DueDate}; db.Settlements.Add(settlement); await db.SaveChangesAsync(ctx.RequestAborted); await audit.Add(ctx.UserId(),"settlement_created","settlement",settlement.Id,new{settlement.CounterpartyId,settlement.CashboxId,settlement.RepresentativeId,settlement.Direction,settlement.BasisType,settlement.BasisReference,settlement.TotalAmount}); return Results.Created($"/api/settlements/{settlement.Id}",settlement);
});
settlements.MapGet("/{id:long}/history", [Authorize] async (long id,CashDb db, HttpContext ctx, ICashboxAccess access) => { var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted); var visible=await db.Settlements.AnyAsync(s=>s.Id==id&&(ctx.User.IsInRole(Admin)||(s.CashboxId.HasValue&&allowed.Contains(s.CashboxId.Value))||s.Allocations.Any(a=>allowed.Contains(a.Operation.CashboxId))),ctx.RequestAborted); if(!visible)return Results.NotFound(); return Results.Ok(await db.PaymentAllocations.AsNoTracking().Where(x=>x.SettlementId==id&&allowed.Contains(x.Operation.CashboxId)).Include(x=>x.Operation).OrderBy(x=>x.CreatedAt).Select(x=>new{x.Id,x.SettlementId,x.OperationId,x.Amount,x.CreatedAt,operationType=x.Operation.Type,cashboxId=x.Operation.CashboxId,documentNumber=x.Operation.DocumentNumber}).ToListAsync()); });
settlements.MapPost("/{id:long}/cancel", [Authorize(Roles = Admin + "," + Accountant)] async (long id, CancelInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
{
    var reason=x.Reason?.Trim();
    if(string.IsNullOrWhiteSpace(reason)||reason.Length>500||reason.Contains('\0'))return Results.Problem("Укажите причину отмены (до 500 символов).",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var s=await db.Settlements.FromSqlInterpolated($"SELECT * FROM \"Settlements\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
    if(s is null)return Results.NotFound();
    if(s.CashboxId.HasValue)await access.Require(ctx.UserId(),s.CashboxId.Value,ctx.RequestAborted);else if(!ctx.User.IsInRole(Admin))return Results.NotFound();
    if(s.Status!="open"||s.PaidAmount!=0||await db.PaymentAllocations.AnyAsync(a=>a.SettlementId==id,ctx.RequestAborted))
        return Results.Problem("Отменить можно только задолженность без платежей. Оплаченные суммы исправляются сторно платёжного документа.",statusCode:409);
    s.Status="cancelled";await db.SaveChangesAsync(ctx.RequestAborted);
    await audit.Add(ctx.UserId(),"settlement_cancelled","settlement",id,new{s.TotalAmount,reason});
    await tx.CommitAsync(ctx.RequestAborted);
    return Results.Ok(new{s.Id,s.Status,s.TotalAmount,s.PaidAmount,remaining=0m});
});
settlements.MapPost("/{id:long}/allocate", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (long id, PaymentAllocationInput x, HttpContext ctx, CashDb db, Audit audit) =>
{
    if(!Money.IsValid(x.Amount))return Results.BadRequest(); await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted,ctx.RequestAborted);
    var settlement=await db.Settlements.FromSqlInterpolated($"SELECT * FROM \"Settlements\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted); if(settlement is null)return Results.NotFound(); var access=ctx.RequestServices.GetRequiredService<ICashboxAccess>(); if(settlement.CashboxId.HasValue)await access.Require(ctx.UserId(),settlement.CashboxId.Value,ctx.RequestAborted); else if(!ctx.User.IsInRole(Admin))return Results.NotFound();
    if(settlement.Status=="cancelled")return Results.Problem("Задолженность отменена; платёж распределить нельзя.",statusCode:409);
    var operation=await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={x.OperationId} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted); if(operation is null||operation.Status!="posted"||operation.CounterpartyId!=settlement.CounterpartyId)return Results.Problem("Платёж должен быть проведён и принадлежать тому же контрагенту.",statusCode:400); await access.Require(ctx.UserId(),operation.CashboxId,ctx.RequestAborted);
    var expectedType=settlement.Direction=="receivable"?"income":"expense"; if(operation.Type!=expectedType)return Results.Problem("Направление платежа не соответствует задолженности.",statusCode:400);
    if(settlement.CashboxId.HasValue)
    {
        var currencies=await db.Cashboxes.AsNoTracking().Where(c=>c.Id==settlement.CashboxId.Value||c.Id==operation.CashboxId).Select(c=>new{c.Id,c.CurrencyId}).ToDictionaryAsync(c=>c.Id,c=>c.CurrencyId,ctx.RequestAborted);
        if(!currencies.TryGetValue(settlement.CashboxId.Value,out var debtCurrency)||!currencies.TryGetValue(operation.CashboxId,out var paymentCurrency)||debtCurrency!=paymentCurrency)return Results.Problem("Валюта кассы платежа не совпадает с валютой задолженности.",statusCode:400);
    }
    if(await db.PaymentAllocations.AnyAsync(a=>a.SettlementId==id&&a.OperationId==x.OperationId,ctx.RequestAborted))return Results.Conflict();
    var alreadyAllocated=await db.PaymentAllocations.Where(a=>a.OperationId==x.OperationId).SumAsync(a=>(decimal?)a.Amount,ctx.RequestAborted)??0m;
    if(settlement.PaidAmount+x.Amount>settlement.TotalAmount||alreadyAllocated+x.Amount>operation.Amount)return Results.Problem("Сумма платежа превышает остаток задолженности или нераспределённую сумму операции.",statusCode:409);
    var allocation=new PaymentAllocation{SettlementId=id,OperationId=x.OperationId,Amount=x.Amount,CreatedBy=ctx.UserId()}; settlement.PaidAmount+=x.Amount; settlement.Status=settlement.PaidAmount==settlement.TotalAmount?"paid":"partially_paid"; db.PaymentAllocations.Add(allocation); await db.SaveChangesAsync(ctx.RequestAborted); await audit.Add(ctx.UserId(),"settlement_payment_allocated","settlement",id,new{allocation.OperationId,allocation.Amount,settlement.PaidAmount,settlement.Status}); await tx.CommitAsync(ctx.RequestAborted); return Results.Ok(new{settlement.Id,settlement.TotalAmount,settlement.PaidAmount,remaining=settlement.TotalAmount-settlement.PaidAmount,settlement.Status});
});
        return app;
    }
}
