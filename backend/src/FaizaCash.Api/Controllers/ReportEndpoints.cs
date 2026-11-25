using FaizaCash.Application;
using FaizaCash.Domain;
using FaizaCash.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.Text;
namespace FaizaCash.Api;

public sealed class ReportFilters
{
    public long CashboxId {get;set;}
    public DateOnly From {get;set;}
    public DateOnly To {get;set;}
    public string? Type {get;set;}
    public string? Status {get;set;}
    public long? CategoryId {get;set;}
    public long? CounterpartyId {get;set;}
    public string? Q {get;set;}
    public string? Sort {get;set;}
    public int? Page {get;set;}
    public int? PageSize {get;set;}
}

public sealed class SettlementFilters
{
    public long? CounterpartyId {get;set;}
    public Guid? RepresentativeId {get;set;}
    public string? Direction {get;set;}
    public string? Status {get;set;}
    public string? Q {get;set;}
    public bool? OverdueOnly {get;set;}
    public DateOnly? AsOf {get;set;}
    public int? Page {get;set;}
    public int? PageSize {get;set;}
}

public static class ReportEndpoints
{
    private const int MaxExportRows=10000;
    /// <summary>ReportLine.Type for a cashbox initial balance that becomes effective inside the period.</summary>
    public const string OpeningLine="opening";
    private static string Money2(decimal value)=>value.ToString("0.00",CultureInfo.InvariantCulture);
    private const string SettlementRoles="admin,accountant,manager,auditor";

    public static void MapReports(this WebApplication app)
    {
        var group=app.MapGroup("/api/reports").RequireAuthorization();

        group.MapGet("/branch.pdf",async(long branchId,DateOnly from,DateOnly to,CashDb db,HttpContext ctx,Audit audit,IPdfReport pdf,ICashboxAccess access,IBalanceService balances)=>
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var report=await BuildBranch(branchId,from,to,db,ctx,access,balances);
            if(report is null)return Results.NotFound();
            var organization=await db.Organizations.AsNoTracking().SingleAsync(ctx.RequestAborted);
            var bytes=pdf.Generate(organization.Name,organization.Currency,"Отчёт по филиалу",$"{report.BranchName} · {from:dd.MM.yyyy} — {to:dd.MM.yyyy} · {report.Count} операций",report.Lines,report.Opening);
            await audit.Add(ctx.UserId(),"pdf_exported","branch_report",branchId,new{from,to,report.Count,report.BoxIds});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.File(bytes,"application/pdf","Faiza-Branch-Report.pdf");
        });
        group.MapGet("/branch.csv",async(long branchId,DateOnly from,DateOnly to,CashDb db,HttpContext ctx,Audit audit,ICashboxAccess access,IBalanceService balances)=>
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var report=await BuildBranch(branchId,from,to,db,ctx,access,balances);
            if(report is null)return Results.NotFound();
            var csv=new StringBuilder("Дата;Номер;Содержание;Контрагент;Приход;Расход;Остаток\r\n");
            csv.Append(";;"+Cell($"Отчёт по филиалу {report.BranchName}, {from:dd.MM.yyyy} — {to:dd.MM.yyyy}; сформирован {DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC")+";;;;\r\n");
            csv.Append(";;"+Cell("Остаток на начало")+";;;;"+Money2(report.Opening)+"\r\n");
            foreach(var line in report.Lines)
                csv.Append(string.Join(';',Cell(line.Date.ToString("dd.MM.yyyy")),Cell(line.Number),Cell(line.Description),Cell(line.Counterparty),line.Type=="income"?Money2(line.Amount):"",line.Type=="expense"?Money2(line.Amount):"",Money2(line.Balance??0m))+"\r\n");
            csv.Append(";;"+Cell("Итоги периода / остаток на конец")+";;"+Money2(report.Income)+";"+Money2(report.Expense)+";"+Money2(report.Closing)+"\r\n");
            await audit.Add(ctx.UserId(),"report_exported","branch_report",branchId,new{from,to,report.Count,report.BoxIds,format="csv"});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.File(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),"text/csv; charset=utf-8","Faiza-Branch-Report.csv");
        });

        group.MapGet("/transfers.pdf",async(HttpContext ctx,CashDb db,Audit audit,IPdfReport pdf,ICashboxAccess access,long? cashboxId,DateOnly? from,DateOnly? to)=>
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var (lines,count)=await BuildTransfers(ctx,db,access,cashboxId,from,to);
            var organization=await db.Organizations.AsNoTracking().SingleAsync(ctx.RequestAborted);
            var subtitle=$"{from?.ToString("dd.MM.yyyy")??"начало"} — {to?.ToString("dd.MM.yyyy")??"сегодня"} · {count} переводов";
            var bytes=pdf.Generate(organization.Name,organization.Currency,"Отчёт по межкассовым переводам",subtitle,lines,balances:false);
            await audit.Add(ctx.UserId(),"pdf_exported","transfer_report",null,new{cashboxId,from,to,count});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.File(bytes,"application/pdf","Faiza-Transfers.pdf");
        });

        group.MapGet("/settlements.pdf",async([AsParameters]SettlementFilters f,CashDb db,HttpContext ctx,Audit audit,IPdfReport pdf,ICashboxAccess access)=>
        {
            var date=ValidateSettlementFilters(f);
            var query=await SettlementQuery(db,ctx,access,f,date);
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var count=await query.CountAsync(ctx.RequestAborted);
            if(count>MaxExportRows)throw new BusinessException($"Экспорт ограничен {MaxExportRows} строками. Уточните фильтр.",413);
            var rows=await SettlementRows(db,query,date).ToListAsync(ctx.RequestAborted);
            var lines=rows.Select(x=>new ReportLine(
                $"ЗД №{x.Id}",x.DueDate,
                $"Касса: {x.Cashbox??"не назначена"}; представитель: {x.Representative??"—"}; {x.BasisType} {x.BasisReference}; статус: {x.Status}; всего: {x.TotalAmount:N2}; оплачено: {x.PaidAmount:N2}; остаток: {x.Remaining:N2}",
                x.Direction=="receivable"?"income":"expense",x.Remaining,x.Counterparty)).ToList();
            var organization=await db.Organizations.AsNoTracking().SingleAsync(ctx.RequestAborted);
            var bytes=pdf.Generate(organization.Name,organization.Currency,"Отчёт по взаиморасчётам",$"По состоянию на {date:dd.MM.yyyy} · {count} записей",lines,balances:false);
            await audit.Add(ctx.UserId(),"pdf_exported","settlement_report",null,new{f.CounterpartyId,f.RepresentativeId,f.Direction,f.Status,f.Q,f.OverdueOnly,date,count});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.File(bytes,"application/pdf","Faiza-Settlements.pdf");
        }).RequireAuthorization(new AuthorizeAttribute{Roles=SettlementRoles});

        group.MapGet("/settlements",async([AsParameters]SettlementFilters f,CashDb db,HttpContext ctx,ICashboxAccess access)=>
        {
            var date=ValidateSettlementFilters(f);
            var query=await SettlementQuery(db,ctx,access,f,date);
            var open=query.Where(x=>x.Status!="cancelled");
            var totals=await open.GroupBy(_=>1).Select(g=>new
            {
                receivableTotal=g.Where(x=>x.Direction=="receivable").Sum(x=>x.TotalAmount),
                receivablePaid=g.Where(x=>x.Direction=="receivable").Sum(x=>x.PaidAmount),
                receivableRemaining=g.Where(x=>x.Direction=="receivable").Sum(x=>x.TotalAmount-x.PaidAmount),
                receivableOverdue=g.Where(x=>x.Direction=="receivable"&&x.Status!="paid"&&x.DueDate<date).Sum(x=>x.TotalAmount-x.PaidAmount),
                payableTotal=g.Where(x=>x.Direction=="payable").Sum(x=>x.TotalAmount),
                payablePaid=g.Where(x=>x.Direction=="payable").Sum(x=>x.PaidAmount),
                payableRemaining=g.Where(x=>x.Direction=="payable").Sum(x=>x.TotalAmount-x.PaidAmount),
                payableOverdue=g.Where(x=>x.Direction=="payable"&&x.Status!="paid"&&x.DueDate<date).Sum(x=>x.TotalAmount-x.PaidAmount)
            }).SingleOrDefaultAsync(ctx.RequestAborted)??new
            {
                receivableTotal=0m,receivablePaid=0m,receivableRemaining=0m,receivableOverdue=0m,
                payableTotal=0m,payablePaid=0m,payableRemaining=0m,payableOverdue=0m
            };
            var total=await query.CountAsync(ctx.RequestAborted);var currentPage=Math.Clamp(f.Page??1,1,1000000);var size=Math.Clamp(f.PageSize??100,1,500);
            var items=await SettlementRows(db,query,date).Skip((currentPage-1)*size).Take(size).ToListAsync(ctx.RequestAborted);
            return Results.Ok(new{asOf=date,page=currentPage,pageSize=size,total,summary=totals,items});
        }).RequireAuthorization(new AuthorizeAttribute{Roles=SettlementRoles});

        group.MapGet("/settlements.csv",async([AsParameters]SettlementFilters f,CashDb db,HttpContext ctx,Audit audit,ICashboxAccess access)=>
        {
            var date=ValidateSettlementFilters(f);
            var query=await SettlementQuery(db,ctx,access,f,date);
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var count=await query.CountAsync(ctx.RequestAborted);
            if(count>MaxExportRows)throw new BusinessException($"Экспорт ограничен {MaxExportRows} строками. Уточните фильтр.",413);
            var rows=await SettlementRows(db,query,date).ToListAsync(ctx.RequestAborted);
            var csv=new StringBuilder("№;Касса;Контрагент;Представитель;Направление;Основание;Документ;Сумма;Оплачено;Остаток;Срок;Статус;Просрочена\r\n");
            foreach(var x in rows)csv.Append(string.Join(';',x.Id.ToString(CultureInfo.InvariantCulture),Cell(x.Cashbox),Cell(x.Counterparty),Cell(x.Representative),Cell(x.Direction=="receivable"?"К получению":"К оплате"),Cell(x.BasisType),Cell(x.BasisReference),Money2(x.TotalAmount),Money2(x.PaidAmount),Money2(x.Remaining),Cell(x.DueDate.ToString("dd.MM.yyyy")),Cell(x.Status),x.Overdue?"да":"нет")+"\r\n");
            csv.Append(Cell($"По состоянию на {date:dd.MM.yyyy}; записей: {count}; сформирован {DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC")+"\r\n");
            await audit.Add(ctx.UserId(),"report_exported","settlement_report",null,new{f.CounterpartyId,f.RepresentativeId,f.Direction,f.Status,f.Q,f.OverdueOnly,date,count,format="csv"});
            await tx.CommitAsync(ctx.RequestAborted);
            return CsvFile(csv,"Faiza-Settlements.csv");
        }).RequireAuthorization(new AuthorizeAttribute{Roles=SettlementRoles});

        group.MapGet("/transfers.csv",async(HttpContext ctx,CashDb db,Audit audit,ICashboxAccess access,long? cashboxId,DateOnly? from,DateOnly? to)=>
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var (lines,count)=await BuildTransfers(ctx,db,access,cashboxId,from,to);
            var csv=new StringBuilder("Дата;Документ;Маршрут и основание;Приход;Расход\r\n");
            foreach(var line in lines)csv.Append(string.Join(';',Cell(line.Date.ToString("dd.MM.yyyy")),Cell(line.Number),Cell(line.Description),line.Type=="income"?Money2(line.Amount):"",line.Type=="expense"?Money2(line.Amount):"")+"\r\n");
            csv.Append(Cell($"Период {from?.ToString("dd.MM.yyyy")??"начало"} — {to?.ToString("dd.MM.yyyy")??"сегодня"}; переводов: {count}")+"\r\n");
            await audit.Add(ctx.UserId(),"report_exported","transfer_report",null,new{cashboxId,from,to,count,format="csv"});
            await tx.CommitAsync(ctx.RequestAborted);
            return CsvFile(csv,"Faiza-Transfers.csv");
        });

        // Shift report (ТЗ §5.7): expected balance comes from IBalanceService, difference is the stored closing difference.
        group.MapGet("/shifts",async(long cashboxId,DateOnly from,DateOnly to,CashDb db,HttpContext ctx,ICashboxAccess access,IBalanceService balances)=>
            Results.Ok(await BuildShifts(cashboxId,from,to,db,ctx,access,balances)));
        group.MapGet("/shifts.csv",async(long cashboxId,DateOnly from,DateOnly to,CashDb db,HttpContext ctx,Audit audit,ICashboxAccess access,IBalanceService balances)=>
        {
            var report=await BuildShifts(cashboxId,from,to,db,ctx,access,balances);
            var csv=new StringBuilder("Смена;Дата;Статус;Начальный остаток;Расчётный остаток;Фактический остаток;Расхождение;Открыл;Закрыл\r\n");
            foreach(var x in report.Items)csv.Append(string.Join(';',x.Id.ToString(CultureInfo.InvariantCulture),Cell(x.Date.ToString("dd.MM.yyyy")),Cell(x.Status=="open"?"Открыта":"Закрыта"),Money2(x.OpeningBalance),Money2(x.ExpectedBalance),x.ClosingBalance.HasValue?Money2(x.ClosingBalance.Value):"",x.ClosingDifference.HasValue?Money2(x.ClosingDifference.Value):"",Cell(x.OpenedBy),Cell(x.ClosedBy))+"\r\n");
            csv.Append(Cell($"Касса {report.Cashbox}, {from:dd.MM.yyyy} — {to:dd.MM.yyyy}; сумма расхождений: {Money2(report.Summary.DiscrepancyTotal)}")+"\r\n");
            await audit.Add(ctx.UserId(),"report_exported","shift_report",cashboxId,new{from,to,count=report.Items.Count,format="csv"});
            return CsvFile(csv,"Faiza-Shifts.csv");
        });
        group.MapGet("/shifts.pdf",async(long cashboxId,DateOnly from,DateOnly to,CashDb db,HttpContext ctx,Audit audit,IPdfReport pdf,ICashboxAccess access,IBalanceService balances)=>
        {
            var report=await BuildShifts(cashboxId,from,to,db,ctx,access,balances);
            var lines=report.Items.Select(x=>new ReportLine($"Смена №{x.Id}",x.Date,$"{(x.Status=="open"?"Открыта":"Закрыта")}; начало {x.OpeningBalance:N2}; расчёт {x.ExpectedBalance:N2}; факт {(x.ClosingBalance.HasValue?x.ClosingBalance.Value.ToString("N2"):"—")}; открыл {x.OpenedBy}; закрыл {x.ClosedBy??"—"}",
                (x.ClosingDifference??0)>=0?"income":"expense",Math.Abs(x.ClosingDifference??0),"Расхождение")).ToList();
            var organization=await db.Organizations.AsNoTracking().SingleAsync(ctx.RequestAborted);
            var bytes=pdf.Generate(organization.Name,organization.Currency,"Отчёт по кассовым сменам",$"{report.Cashbox} · {from:dd.MM.yyyy} — {to:dd.MM.yyyy} · смен: {report.Items.Count}; излишки в колонке «Приход», недостачи — «Расход»",lines,balances:false);
            await audit.Add(ctx.UserId(),"pdf_exported","shift_report",cashboxId,new{from,to,count=report.Items.Count});
            return Results.File(bytes,"application/pdf","Faiza-Shifts.pdf");
        });

        // Representative report (ТЗ §5.7): money collected/handed over/accepted per representative within the user's cashbox scope.
        group.MapGet("/representatives",async(DateOnly from,DateOnly to,Guid? representativeId,CashDb db,HttpContext ctx,ICashboxAccess access)=>
            Results.Ok(await BuildRepresentatives(from,to,representativeId,db,ctx,access))).RequireAuthorization(new AuthorizeAttribute{Roles=SettlementRoles});
        group.MapGet("/representatives.csv",async(DateOnly from,DateOnly to,Guid? representativeId,CashDb db,HttpContext ctx,Audit audit,ICashboxAccess access)=>
        {
            var report=await BuildRepresentatives(from,to,representativeId,db,ctx,access);
            var csv=new StringBuilder("Представитель;Квитанций;У представителя;Передано кассиру;Принято кассой;Отменено\r\n");
            foreach(var x in report.Items)csv.Append(string.Join(';',Cell(x.Representative),x.Count.ToString(CultureInfo.InvariantCulture),Money2(x.Collected),Money2(x.HandedOver),Money2(x.Accepted),Money2(x.Cancelled))+"\r\n");
            csv.Append(Cell($"Период {from:dd.MM.yyyy} — {to:dd.MM.yyyy}; сформирован {DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC")+"\r\n");
            await audit.Add(ctx.UserId(),"report_exported","representative_report",null,new{from,to,representativeId,format="csv"});
            return CsvFile(csv,"Faiza-Representatives.csv");
        }).RequireAuthorization(new AuthorizeAttribute{Roles=SettlementRoles});
        group.MapGet("/representatives.pdf",async(DateOnly from,DateOnly to,Guid? representativeId,CashDb db,HttpContext ctx,Audit audit,IPdfReport pdf,ICashboxAccess access)=>
        {
            var report=await BuildRepresentatives(from,to,representativeId,db,ctx,access);
            var lines=report.Receipts.Select(x=>new ReportLine($"Квитанция №{x.Id}",DateOnly.FromDateTime(x.CreatedAt),$"{x.Representative}: {x.BasisType} {x.BasisReference}; статус: {x.Status}",x.Status=="cancelled"?"cancelled":"income",x.Amount,x.Counterparty)).ToList();
            var organization=await db.Organizations.AsNoTracking().SingleAsync(ctx.RequestAborted);
            var bytes=pdf.Generate(organization.Name,organization.Currency,"Отчёт по торговым представителям",$"{from:dd.MM.yyyy} — {to:dd.MM.yyyy} · квитанций: {report.Receipts.Count} (отменённые не входят в итог)",lines,balances:false);
            await audit.Add(ctx.UserId(),"pdf_exported","representative_report",null,new{from,to,representativeId});
            return Results.File(bytes,"application/pdf","Faiza-Representatives.pdf");
        }).RequireAuthorization(new AuthorizeAttribute{Roles=SettlementRoles});

        group.MapGet("/cashbook",async([AsParameters]ReportFilters f,HttpContext ctx,CashDb db,ICashboxAccess access,IBalanceService balances)=>
        {
            Validate(f);await access.Require(ctx.UserId(),f.CashboxId,ctx.RequestAborted);
            if(f.Type!=null||f.CategoryId.HasValue||f.CounterpartyId.HasValue||!string.IsNullOrEmpty(f.Q)||f.Status is "all" or "draft")throw new BusinessException("Кассовая книга включает все проведённые документы за период. Для дополнительных фильтров используйте журнал операций.",400);
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var period=await balances.PeriodOpening([f.CashboxId],f.From,f.To,ctx.RequestAborted);
            var opening=period.Opening;
            var activationDate=period.Activations.Count>0?period.Activations.Keys.Min():DateOnly.MaxValue;
            var activationAmount=period.ActivationTotal;
            var ledger=Filtered(db,f,true);
            var total=await ledger.CountAsync(ctx.RequestAborted);var page=Math.Clamp(f.Page??1,1,1000000);var size=Math.Clamp(f.PageSize??15,1,100);
            var rows=await ledger.OrderBy(x=>x.Date).ThenBy(x=>x.PostingSeq).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).Select(x=>new {
                x.Id,x.Date,x.DocumentNumber,x.Description,counterparty=x.Counterparty==null?null:x.CounterpartyNameSnapshot??x.Counterparty.Name,x.Type,x.Amount,
                balance=opening+(x.Date>=activationDate?activationAmount:0m)+db.Operations.Where(p=>p.CashboxId==f.CashboxId&&p.Status=="posted"&&p.Date>=f.From&&(p.Date<x.Date || p.Date==x.Date&&(p.PostingSeq<x.PostingSeq || p.PostingSeq==x.PostingSeq&&p.Id<=x.Id))).Sum(p=>p.Type=="income"?p.Amount:-p.Amount)
            }).ToListAsync(ctx.RequestAborted);
            var (income,expense)=await Totals(ledger,ctx.RequestAborted);
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.Ok(new {page,pageSize=size,total,opening,income,expense,activationAmount,activationDate=period.Activations.Count>0?activationDate:(DateOnly?)null,closing=opening+activationAmount+income-expense,items=rows});
        });

        group.MapGet("/cash.csv",async([AsParameters]ReportFilters f,CashDb db,HttpContext ctx,Audit audit,ICashboxAccess access,IBalanceService balances)=>
        {
            Validate(f);await access.Require(ctx.UserId(),f.CashboxId,ctx.RequestAborted);
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var query=Filtered(db,f);var count=await query.CountAsync(ctx.RequestAborted);
            if(count>MaxExportRows)throw new BusinessException($"Экспорт ограничен {MaxExportRows} строками. Уточните фильтр периода.",413);
            var (income,expense)=await Totals(query,ctx.RequestAborted);
            var book=IsBook(f);
            var period=book?await balances.PeriodOpening([f.CashboxId],f.From,f.To,ctx.RequestAborted):new PeriodOpening(0m,new Dictionary<DateOnly,decimal>());
            var balance=period.Opening;
            var pendingActivations=new SortedDictionary<DateOnly,decimal>(period.Activations.ToDictionary(x=>x.Key,x=>x.Value));
            ctx.Response.ContentType="text/csv; charset=utf-8";
            ctx.Response.Headers.ContentDisposition="attachment; filename=\"Faiza-Cash.csv\"";
            await ctx.Response.StartAsync(ctx.RequestAborted);
            await using var writer=new StreamWriter(ctx.Response.Body,new UTF8Encoding(true),4096,leaveOpen:true);
            await writer.WriteAsync("Дата;Номер;Тип;Содержание;Контрагент;Категория;Статус;Приход;Расход"+(book?";Остаток после операции":"")+"\r\n");
            if(book)await writer.WriteAsync(";;;"+Cell("Остаток на начало")+";;;;;;"+Money2(balance)+"\r\n");
            async Task WriteActivationsUntil(DateOnly date)
            {
                foreach(var day in pendingActivations.Keys.Where(d=>d<=date).ToList())
                {
                    balance+=pendingActivations[day];
                    await writer.WriteAsync(string.Join(';',Cell(day.ToString("dd.MM.yyyy")),Cell("Начальный остаток"),Cell("Активация кассы"),Cell("Начальный остаток кассы: "+Money2(pendingActivations[day])),"","","","","")+";"+Money2(balance)+"\r\n");
                    pendingActivations.Remove(day);
                }
            }
            var rows=Ordered(query,f.Sort).Include(x=>x.Counterparty).Include(x=>x.Category).AsAsyncEnumerable();
            await foreach(var row in rows.WithCancellation(ctx.RequestAborted))
            {
                if(book)await WriteActivationsUntil(row.Date);
                var line=string.Join(';',Cell(row.Date.ToString("dd.MM.yyyy")),Cell(row.DocumentNumber??$"Черновик №{row.Id}"),Cell(row.Type=="income"?"Приход":"Расход"),Cell(row.Description),Cell(row.CounterpartyNameSnapshot??row.Counterparty?.Name),Cell(row.CategoryNameSnapshot??row.Category.Name),Cell(row.Status=="posted"?"Проведено":"Черновик"),row.Type=="income"?Money2(row.Amount):"",row.Type=="expense"?Money2(row.Amount):"");
                if(book){balance+=Money.Signed(row.Type,row.Amount);line+=";"+Money2(balance);}
                await writer.WriteAsync(line+"\r\n");
            }
            if(book)
            {
                await WriteActivationsUntil(f.To);
                await writer.WriteAsync(";;;"+Cell("Итоги периода / остаток на конец")+";;;;"+Money2(income)+";"+Money2(expense)+";"+Money2(balance)+"\r\n");
            }
            await writer.FlushAsync(ctx.RequestAborted);
            await audit.Add(ctx.UserId(),"report_exported","report",null,new{f.CashboxId,f.From,f.To,count});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.Empty;
        });

        group.MapGet("/cash.pdf",async([AsParameters]ReportFilters f,CashDb db,HttpContext ctx,Audit audit,IPdfReport pdf,ICashboxAccess access,IBalanceService balances)=>
        {
            Validate(f);await access.Require(ctx.UserId(),f.CashboxId,ctx.RequestAborted);
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var query=Filtered(db,f);var count=await query.CountAsync(ctx.RequestAborted);
            if(count>MaxExportRows)throw new BusinessException($"Экспорт ограничен {MaxExportRows} строками. Уточните фильтр периода.",413);
            var org=await db.Organizations.SingleAsync(ctx.RequestAborted);var box=await db.Cashboxes.SingleOrDefaultAsync(x=>x.Id==f.CashboxId,ctx.RequestAborted)??throw new KeyNotFoundException();
            var rows=await Ordered(query,f.Sort).Include(x=>x.Counterparty).ToListAsync(ctx.RequestAborted);
            var isBook=IsBook(f);
            List<ReportLine> lines;decimal opening=0m;
            if(isBook)
            {
                var period=await balances.PeriodOpening([f.CashboxId],f.From,f.To,ctx.RequestAborted);opening=period.Opening;
                lines=WithRunningBalance(opening,period.Activations,rows.Select(x=>(x,box.Name)),$"{box.Name}: начальный остаток кассы",withPrefix:false);
            }
            else lines=rows.Select(x=>new ReportLine(x.DocumentNumber??$"Черновик №{x.Id}",x.Date,x.Description,x.Type,x.Amount,x.CounterpartyNameSnapshot??x.Counterparty?.Name,null,x.Status)).ToList();
            var bytes=pdf.Generate(org.Name,org.Currency,isBook?"Учебная кассовая книга":"Отфильтрованный журнал операций",$"{box.Name} · {f.From:dd.MM.yyyy} — {f.To:dd.MM.yyyy} · Все найденные записи",lines,opening,balances:isBook);
            await audit.Add(ctx.UserId(),"pdf_exported","report",null,new{f.CashboxId,f.From,f.To,count});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.File(bytes,"application/pdf","Faiza-Cash.pdf");
        });
    }

    private static IResult CsvFile(StringBuilder csv,string name)=>Results.File(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),"text/csv; charset=utf-8",name);

    internal sealed record ShiftRow(long Id,DateOnly Date,string Status,decimal OpeningBalance,decimal ExpectedBalance,decimal? ClosingBalance,decimal? ClosingDifference,string OpenedBy,string? ClosedBy,DateTime OpenedAt,DateTime? ClosedAt);
    internal sealed record ShiftSummary(int Open,int Closed,int WithDiscrepancy,decimal DiscrepancyTotal);
    internal sealed record ShiftReport(long CashboxId,string Cashbox,DateOnly From,DateOnly To,ShiftSummary Summary,List<ShiftRow> Items);

    private static async Task<ShiftReport> BuildShifts(long cashboxId,DateOnly from,DateOnly to,CashDb db,HttpContext ctx,ICashboxAccess access,IBalanceService balances)
    {
        ValidatePeriod(from,to);
        await access.Require(ctx.UserId(),cashboxId,ctx.RequestAborted);
        var box=await db.Cashboxes.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==cashboxId,ctx.RequestAborted)??throw new KeyNotFoundException();
        var shifts=await db.Shifts.AsNoTracking().Where(x=>x.CashboxId==cashboxId&&x.Date>=from&&x.Date<=to).OrderBy(x=>x.Date).ThenBy(x=>x.Id).Take(MaxExportRows).ToListAsync(ctx.RequestAborted);
        var userIds=shifts.Select(x=>x.OpenedBy).Concat(shifts.Where(x=>x.ClosedBy.HasValue).Select(x=>x.ClosedBy!.Value)).Distinct().ToArray();
        var users=await db.Users.AsNoTracking().Where(x=>userIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.FullName,ctx.RequestAborted);
        var rows=new List<ShiftRow>(shifts.Count);
        foreach(var x in shifts)
        {
            // Closed shift: expected balance recorded at closing (closing - difference). Open shift: current ledger balance.
            var expected=x.Status=="closed"&&x.ClosingBalance.HasValue&&x.ClosingDifference.HasValue?x.ClosingBalance.Value-x.ClosingDifference.Value:await balances.BalanceAt(cashboxId,x.Date,ctx.RequestAborted);
            rows.Add(new ShiftRow(x.Id,x.Date,x.Status,x.OpeningBalance,expected,x.ClosingBalance,x.ClosingDifference,users.GetValueOrDefault(x.OpenedBy,"—"),x.ClosedBy.HasValue?users.GetValueOrDefault(x.ClosedBy.Value,"—"):null,x.OpenedAt,x.ClosedAt));
        }
        var summary=new ShiftSummary(rows.Count(x=>x.Status=="open"),rows.Count(x=>x.Status=="closed"),rows.Count(x=>(x.ClosingDifference??0)!=0),rows.Sum(x=>x.ClosingDifference??0));
        return new ShiftReport(cashboxId,box.Name,from,to,summary,rows);
    }

    internal sealed record RepresentativeTotals(Guid RepresentativeId,string Representative,int Count,decimal Collected,decimal HandedOver,decimal Accepted,decimal Cancelled);
    internal sealed record RepresentativeReceiptRow(long Id,Guid RepresentativeId,string Representative,string Counterparty,string BasisType,string BasisReference,decimal Amount,string Status,DateTime CreatedAt,long CashboxId);
    internal sealed record RepresentativeReport(DateOnly From,DateOnly To,List<RepresentativeTotals> Items,List<RepresentativeReceiptRow> Receipts);

    private static async Task<RepresentativeReport> BuildRepresentatives(DateOnly from,DateOnly to,Guid? representativeId,CashDb db,HttpContext ctx,ICashboxAccess access)
    {
        ValidatePeriod(from,to);
        var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
        // Business dates are Asia/Dushanbe (UTC+5): convert the inclusive date range to UTC instants.
        var startUtc=from.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc).AddHours(-5);var endUtc=to.AddDays(1).ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc).AddHours(-5);
        var receipts=await db.RepresentativeReceipts.AsNoTracking().Where(x=>allowed.Contains(x.CashboxId)&&x.CreatedAt>=startUtc&&x.CreatedAt<endUtc&&(!representativeId.HasValue||x.RepresentativeId==representativeId))
            .OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).Take(MaxExportRows)
            .Select(x=>new RepresentativeReceiptRow(x.Id,x.RepresentativeId,db.Users.Where(u=>u.Id==x.RepresentativeId).Select(u=>u.FullName).FirstOrDefault()??"—",x.Counterparty.Name,x.BasisType,x.BasisReference,x.Amount,x.Status,x.CreatedAt,x.CashboxId))
            .ToListAsync(ctx.RequestAborted);
        decimal Sum(IEnumerable<RepresentativeReceiptRow> rows,string status)=>rows.Where(r=>r.Status==status).Sum(r=>r.Amount);
        var totals=receipts.GroupBy(x=>new{x.RepresentativeId,x.Representative}).OrderBy(g=>g.Key.Representative)
            .Select(g=>new RepresentativeTotals(g.Key.RepresentativeId,g.Key.Representative,g.Count(),Sum(g,"collected"),Sum(g,"handed_over"),Sum(g,"accepted"),Sum(g,"cancelled"))).ToList();
        return new RepresentativeReport(from,to,totals,receipts);
    }

    private sealed record BranchReport(string BranchName,int Count,decimal Opening,List<ReportLine> Lines,decimal Income,decimal Expense,decimal Closing,long[] BoxIds);

    /// <summary>Shared branch report used by PDF and CSV; balances come from IBalanceService (FG7-001).</summary>
    private static async Task<BranchReport?> BuildBranch(long branchId,DateOnly from,DateOnly to,CashDb db,HttpContext ctx,ICashboxAccess access,IBalanceService balances)
    {
        ValidatePeriod(from,to);
        var branch=await db.Branches.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==branchId,ctx.RequestAborted);
        if(branch is null)return null;
        var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
        var candidates=await db.Cashboxes.AsNoTracking().Where(x=>x.BranchId==branchId&&allowed.Contains(x.Id)).Select(x=>new{x.Id,x.Name}).ToListAsync(ctx.RequestAborted);
        if(candidates.Count==0)throw new BusinessException("Нет доступных касс в этом филиале.",403);
        var existing=(await balances.ExistingOn(candidates.Select(x=>x.Id).ToArray(),to,ctx.RequestAborted)).ToHashSet();
        var boxes=candidates.Where(x=>existing.Contains(x.Id)).ToList();
        var boxIds=boxes.Select(x=>x.Id).ToArray();var names=boxes.ToDictionary(x=>x.Id,x=>x.Name);
        var query=db.Operations.AsNoTracking().Where(x=>boxIds.Contains(x.CashboxId)&&x.Status=="posted"&&x.Date>=from&&x.Date<=to);
        var count=await query.CountAsync(ctx.RequestAborted);
        if(count>MaxExportRows)throw new BusinessException($"Экспорт ограничен {MaxExportRows} строками. Уточните период.",413);
        var rows=await query.OrderBy(x=>x.Date).ThenBy(x=>x.PostingSeq).ThenBy(x=>x.Id).ToListAsync(ctx.RequestAborted);
        var period=await balances.PeriodOpening(boxIds,from,to,ctx.RequestAborted);
        var lines=WithRunningBalance(period.Opening,period.Activations,rows.Select(x=>(x,x.CashboxNameSnapshot??names[x.CashboxId])),"Начальный остаток кассы, активированной в периоде",withPrefix:true);
        var income=rows.Where(x=>x.Type=="income").Sum(x=>x.Amount);var expense=rows.Where(x=>x.Type=="expense").Sum(x=>x.Amount);
        return new BranchReport(branch.Name,count,period.Opening,lines,income,expense,period.Opening+period.ActivationTotal+income-expense,boxIds);
    }

    /// <summary>Merges activation lines into date-ordered posted operations and computes the running balance.</summary>
    private static List<ReportLine> WithRunningBalance(decimal opening,IReadOnlyDictionary<DateOnly,decimal> activations,IEnumerable<(CashOperation Row,string CashboxName)> rows,string activationText,bool withPrefix)
    {
        var balance=opening;var result=new List<ReportLine>();var pending=new SortedDictionary<DateOnly,decimal>(activations.ToDictionary(x=>x.Key,x=>x.Value));
        void Flush(DateOnly date){foreach(var day in pending.Keys.Where(d=>d<=date).ToList()){balance+=pending[day];result.Add(new ReportLine("Начальный остаток",day,activationText,OpeningLine,pending[day],null,balance));pending.Remove(day);}}
        foreach(var (row,name) in rows.OrderBy(x=>x.Row.Date).ThenBy(x=>x.Row.PostingSeq).ThenBy(x=>x.Row.Id))
        {
            Flush(row.Date);
            balance+=Money.Signed(row.Type,row.Amount);
            result.Add(new ReportLine(row.DocumentNumber??$"Операция №{row.Id}",row.Date,withPrefix?$"{name}: {row.Description}":row.Description,row.Type,row.Amount,row.CounterpartyNameSnapshot??row.Counterparty?.Name,balance,row.Status));
        }
        Flush(DateOnly.MaxValue);
        return result;
    }

    internal static async Task<(List<ReportLine> Lines,int Count)> BuildTransfers(HttpContext ctx,CashDb db,ICashboxAccess access,long? cashboxId,DateOnly? from,DateOnly? to)
    {
        if(from.HasValue&&to.HasValue&&from>to)throw new BusinessException("Укажите корректный период.",400);
        var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
        if(!ctx.User.IsInRole("admin")&&!cashboxId.HasValue)throw new BusinessException("Выберите доступную кассу.",400);
        if(cashboxId.HasValue)await access.Require(ctx.UserId(),cashboxId.Value,ctx.RequestAborted);
        var query=db.Transfers.AsNoTracking().Where(x=>allowed.Contains(x.SourceCashboxId)&&allowed.Contains(x.TargetCashboxId)&&(!cashboxId.HasValue||x.SourceCashboxId==cashboxId||x.TargetCashboxId==cashboxId)&&(!from.HasValue||x.Date>=from)&&(!to.HasValue||x.Date<=to));
        var count=await query.CountAsync(ctx.RequestAborted);
        if((long)count*2>MaxExportRows)throw new BusinessException($"Экспорт ограничен {MaxExportRows} строками. Уточните период.",413);
        var transfers=await query.OrderBy(x=>x.Date).ThenBy(x=>x.Id).ToListAsync(ctx.RequestAborted);
        var ids=transfers.Select(x=>x.Id).ToArray();
        var operations=await db.Operations.AsNoTracking().Where(x=>x.TransferId.HasValue&&ids.Contains(x.TransferId.Value)).ToListAsync(ctx.RequestAborted);
        var boxes=await db.Cashboxes.AsNoTracking().Where(x=>allowed.Contains(x.Id)).Select(x=>new{x.Id,x.Name}).ToDictionaryAsync(x=>x.Id,x=>x.Name,ctx.RequestAborted);
        var lines=new List<ReportLine>(transfers.Count*2);
        foreach(var transfer in transfers)
        {
            var source=operations.FirstOrDefault(x=>x.TransferId==transfer.Id&&x.Type=="expense");
            var target=operations.FirstOrDefault(x=>x.TransferId==transfer.Id&&x.Type=="income");
            if(source is null||target is null)throw new BusinessException($"У перевода №{transfer.Id} отсутствуют связанные кассовые операции.",409);
            var sourceName=source.CashboxNameSnapshot??boxes.GetValueOrDefault(transfer.SourceCashboxId,"Касса №"+transfer.SourceCashboxId);
            var targetName=target.CashboxNameSnapshot??boxes.GetValueOrDefault(transfer.TargetCashboxId,"Касса №"+transfer.TargetCashboxId);
            lines.Add(new(source.DocumentNumber??$"РКО перевода №{transfer.Id}",transfer.Date,$"{sourceName} → {targetName}: {transfer.Description}","expense",transfer.Amount));
            lines.Add(new(target.DocumentNumber??$"ПКО перевода №{transfer.Id}",transfer.Date,$"{sourceName} → {targetName}: {transfer.Description}","income",transfer.Amount));
        }
        return (lines,count);
    }

    internal static DateOnly ValidateSettlementFilters(SettlementFilters f)
    {
        var date=f.AsOf??Local.Today;
        if(f.Direction is not (null or "receivable" or "payable")||f.Status is not (null or "open" or "partially_paid" or "paid" or "cancelled")||f.Q?.Length>160||f.Q?.Contains('\0')==true||date==DateOnly.MinValue)
            throw new BusinessException("Некорректный фильтр отчёта по задолженностям.",400);
        return date;
    }

    internal static async Task<IQueryable<Settlement>> SettlementQuery(CashDb db,HttpContext ctx,ICashboxAccess access,SettlementFilters f,DateOnly date)
    {
        var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);var admin=ctx.User.IsInRole("admin");var q=f.Q;
        return db.Settlements.AsNoTracking().Where(x=>(admin||(x.CashboxId.HasValue&&allowed.Contains(x.CashboxId.Value))||x.Allocations.Any(a=>allowed.Contains(a.Operation.CashboxId)))&&
            (!f.CounterpartyId.HasValue||x.CounterpartyId==f.CounterpartyId)&&
            (!f.RepresentativeId.HasValue||x.RepresentativeId==f.RepresentativeId)&&
            (f.Direction==null||x.Direction==f.Direction)&&
            (f.Status==null||x.Status==f.Status)&&
            (!f.OverdueOnly.GetValueOrDefault()||(x.Status=="open"||x.Status=="partially_paid")&&x.DueDate<date)&&
            (q==null||(x.CounterpartyNameSnapshot!=null&&x.CounterpartyNameSnapshot.Contains(q))||x.Counterparty.Name.Contains(q)||x.BasisReference.Contains(q)));
    }

    internal sealed record SettlementRow(long Id,long CounterpartyId,long? CashboxId,string? Cashbox,string Counterparty,Guid? RepresentativeId,string? Representative,string Direction,string BasisType,string BasisReference,decimal TotalAmount,decimal PaidAmount,decimal Remaining,string Status,DateOnly DueDate,bool Overdue);

    internal static IQueryable<SettlementRow> SettlementRows(CashDb db,IQueryable<Settlement> query,DateOnly date)=>
        query.OrderBy(x=>x.DueDate).ThenBy(x=>x.Id).Select(x=>new SettlementRow(x.Id,x.CounterpartyId,x.CashboxId,x.Cashbox==null?null:x.Cashbox.Name,x.CounterpartyNameSnapshot??x.Counterparty.Name,x.RepresentativeId,
            db.Users.Where(u=>u.Id==x.RepresentativeId).Select(u=>u.FullName).FirstOrDefault(),x.Direction,x.BasisType,x.BasisReference,x.TotalAmount,x.PaidAmount,
            x.Status=="cancelled"?0m:x.TotalAmount-x.PaidAmount,x.Status,x.DueDate,(x.Status=="open"||x.Status=="partially_paid")&&x.DueDate<date));

    private static void Validate(ReportFilters f)
    {
        if(f.CashboxId<=0 || f.From==DateOnly.MinValue || f.From>f.To || f.To.DayNumber-f.From.DayNumber>3660)throw new BusinessException("Укажите кассу и корректный период (не более 10 лет).",400);
        if(f.Type is not (null or "income" or "expense") || f.Status is not (null or "all" or "draft" or "posted") || f.Sort is not (null or "amount_asc" or "amount_desc" or "date_asc" or "date_desc") || f.Q?.Length>1000 || f.Q?.Contains('\0')==true)
            throw new BusinessException("Некорректный фильтр операций.",400);
    }
    internal static void ValidatePeriod(DateOnly from,DateOnly to)
    {
        if(from==DateOnly.MinValue||from>to||to.DayNumber-from.DayNumber>3660)
            throw new BusinessException("Укажите корректный период (не более 10 лет).",400);
    }
    private static IQueryable<CashOperation> Filtered(CashDb db,ReportFilters f,bool posted=false)=>db.Operations.AsNoTracking().Where(x=>x.CashboxId==f.CashboxId&&x.Date>=f.From&&x.Date<=f.To&&(posted?x.Status=="posted":f.Status=="all"||x.Status==(f.Status??"posted"))&&(f.Type==null||x.Type==f.Type)&&(!f.CategoryId.HasValue||x.CategoryId==f.CategoryId)&&(!f.CounterpartyId.HasValue||x.CounterpartyId==f.CounterpartyId)&&(string.IsNullOrEmpty(f.Q)||x.Description.Contains(f.Q)||(x.DocumentNumber??"").Contains(f.Q)));
    private static bool IsBook(ReportFilters f)=>f.Sort==null&&f.Type==null&&f.CategoryId==null&&f.CounterpartyId==null&&string.IsNullOrEmpty(f.Q)&&f.Status is null or "posted";
    private static IOrderedQueryable<CashOperation> Ordered(IQueryable<CashOperation> rows,string? sort)=>sort switch{"amount_asc"=>rows.OrderBy(x=>x.Amount).ThenBy(x=>x.Id),"amount_desc"=>rows.OrderByDescending(x=>x.Amount).ThenBy(x=>x.Id),"date_desc"=>rows.OrderByDescending(x=>x.Date).ThenByDescending(x=>x.Id),"date_asc"=>rows.OrderBy(x=>x.Date).ThenBy(x=>x.Id),_=>rows.OrderBy(x=>x.Date).ThenBy(x=>x.PostingSeq).ThenBy(x=>x.Id)};
    private static async Task<(decimal Income,decimal Expense)> Totals(IQueryable<CashOperation> rows,CancellationToken cancellationToken)
    {
        var totals=await rows.GroupBy(_=>1).Select(group=>new
        {
            Income=group.Where(x=>x.Type=="income").Sum(x=>x.Amount),
            Expense=group.Where(x=>x.Type=="expense").Sum(x=>x.Amount)
        }).SingleOrDefaultAsync(cancellationToken);
        return(totals?.Income??0m,totals?.Expense??0m);
    }
    public static string Cell(string? text)
    {
        text??="";if(text.TrimStart().Length>0&&"=+-@\t\r\n".Contains(text.TrimStart()[0]) || text.Length>0&&"\t\r\n".Contains(text[0]))text="'"+text;
        return "\""+text.Replace("\"","\"\"")+"\"";
    }
}
