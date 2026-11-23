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

public static class ReportEndpoints
{
    private const int MaxExportRows=10000;
    public static void MapReports(this WebApplication app)
    {
        var group=app.MapGroup("/api/reports").RequireAuthorization();
        group.MapGet("/branch.pdf",async(long branchId,DateOnly from,DateOnly to,CashDb db,HttpContext ctx,Audit audit,IPdfReport pdf,ICashboxAccess access)=>
        {
            ValidatePeriod(from,to);
            var branch=await db.Branches.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==branchId,ctx.RequestAborted);
            if(branch is null)return Results.NotFound();
            var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
            var boxes=await db.Cashboxes.AsNoTracking().Where(x=>x.BranchId==branchId&&allowed.Contains(x.Id)).Select(x=>new{x.Id,x.Name,x.Code}).ToListAsync(ctx.RequestAborted);
            if(boxes.Count==0)return Results.Forbid();
            var boxIds=boxes.Select(x=>x.Id).ToArray();
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var query=db.Operations.AsNoTracking().Where(x=>boxIds.Contains(x.CashboxId)&&x.Status=="posted"&&x.Date>=from&&x.Date<=to);
            var count=await query.CountAsync(ctx.RequestAborted);
            if(count>MaxExportRows)throw new BusinessException($"Экспорт ограничен {MaxExportRows} строками. Уточните период.",413);
            var rows=await query.OrderBy(x=>x.Date).ThenBy(x=>x.PostingSeq).ThenBy(x=>x.Id).ToListAsync(ctx.RequestAborted);
            var initial=await db.Openings.Where(x=>boxIds.Contains(x.CashboxId)&&x.Date<from).SumAsync(x=>x.Amount,ctx.RequestAborted);
            var prior=await db.Operations.Where(x=>boxIds.Contains(x.CashboxId)&&x.Status=="posted"&&x.Date<from).SumAsync(x=>x.Type=="income"?x.Amount:-x.Amount,ctx.RequestAborted);
            var opening=initial+prior;var balance=opening;var names=boxes.ToDictionary(x=>x.Id,x=>x.Name);
            var lines=rows.Select(x=>
            {
                balance+=Money.Signed(x.Type,x.Amount);
                var name=x.CashboxNameSnapshot??names[x.CashboxId];
                return new ReportLine(x.DocumentNumber??$"Операция №{x.Id}",x.Date,$"{name}: {x.Description}",x.Type,x.Amount,x.CounterpartyNameSnapshot,balance,x.Status);
            }).ToList();
            var organization=await db.Organizations.AsNoTracking().SingleAsync(ctx.RequestAborted);
            var bytes=pdf.Generate(organization.Name,organization.Currency,"Отчёт по филиалу",$"{branch.Name} · {from:dd.MM.yyyy} — {to:dd.MM.yyyy} · {count} операций",lines,opening);
            await audit.Add(ctx.UserId(),"pdf_exported","branch_report",branchId,new{from,to,count,boxIds});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.File(bytes,"application/pdf","Faiza-Branch-Report.pdf");
        });
        group.MapGet("/transfers.pdf",async(HttpContext ctx,CashDb db,Audit audit,IPdfReport pdf,ICashboxAccess access,long? cashboxId,DateOnly? from,DateOnly? to)=>
        {
            if(from.HasValue&&to.HasValue&&from>to)throw new BusinessException("Укажите корректный период.",400);
            var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
            if(!ctx.User.IsInRole("admin")&&!cashboxId.HasValue)throw new BusinessException("Выберите доступную кассу.",400);
            if(cashboxId.HasValue)await access.Require(ctx.UserId(),cashboxId.Value,ctx.RequestAborted);
            var query=db.Transfers.AsNoTracking().Where(x=>allowed.Contains(x.SourceCashboxId)&&allowed.Contains(x.TargetCashboxId)&&(!cashboxId.HasValue||x.SourceCashboxId==cashboxId||x.TargetCashboxId==cashboxId)&&(!from.HasValue||x.Date>=from)&&(!to.HasValue||x.Date<=to));
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
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
            var organization=await db.Organizations.AsNoTracking().SingleAsync(ctx.RequestAborted);
            var subtitle=$"{from?.ToString("dd.MM.yyyy")??"начало"} — {to?.ToString("dd.MM.yyyy")??"сегодня"} · {count} переводов";
            var bytes=pdf.Generate(organization.Name,organization.Currency,"Отчёт по межкассовым переводам",subtitle,lines,balances:false);
            await audit.Add(ctx.UserId(),"pdf_exported","transfer_report",null,new{cashboxId,from,to,count});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.File(bytes,"application/pdf","Faiza-Transfers.pdf");
        });
        group.MapGet("/settlements.pdf",async(CashDb db,HttpContext ctx,Audit audit,IPdfReport pdf,ICashboxAccess access,long? counterpartyId,string? direction,string? status,string? q,bool? overdueOnly,DateOnly? asOf)=>
        {
            var date=asOf??Local.Today;
            if(direction is not (null or "receivable" or "payable")||status is not (null or "open" or "partially_paid" or "paid")||q?.Length>160||q?.Contains('\0')==true||date==DateOnly.MinValue)
                throw new BusinessException("Некорректный фильтр отчёта по задолженностям.",400);
            var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
            var query=db.Settlements.AsNoTracking().Include(x=>x.Counterparty).Include(x=>x.Cashbox).Where(x=>(ctx.User.IsInRole("admin")||(x.CashboxId.HasValue&&allowed.Contains(x.CashboxId.Value))||x.Allocations.Any(a=>allowed.Contains(a.Operation.CashboxId)))&&
                (!counterpartyId.HasValue||x.CounterpartyId==counterpartyId)&&
                (direction==null||x.Direction==direction)&&
                (status==null||x.Status==status)&&
                (!overdueOnly.GetValueOrDefault()||x.Status!="paid"&&x.DueDate<date)&&
                (q==null||(x.CounterpartyNameSnapshot!=null&&x.CounterpartyNameSnapshot.Contains(q))||x.Counterparty.Name.Contains(q)||x.BasisReference.Contains(q)));
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var count=await query.CountAsync(ctx.RequestAborted);
            if(count>MaxExportRows)throw new BusinessException($"Экспорт ограничен {MaxExportRows} строками. Уточните фильтр.",413);
            var rows=await query.OrderBy(x=>x.DueDate).ThenBy(x=>x.Id).ToListAsync(ctx.RequestAborted);
            var lines=rows.Select(x=>new ReportLine(
                $"ЗД №{x.Id}",x.DueDate,
                $"Касса: {x.Cashbox?.Name??"не назначена"}; {x.BasisType} {x.BasisReference}; статус: {x.Status}; всего: {x.TotalAmount:N2}; оплачено: {x.PaidAmount:N2}; остаток: {x.TotalAmount-x.PaidAmount:N2}",
                x.Direction=="receivable"?"income":"expense",x.TotalAmount-x.PaidAmount,x.CounterpartyNameSnapshot??x.Counterparty.Name)).ToList();
            var organization=await db.Organizations.AsNoTracking().SingleAsync(ctx.RequestAborted);
            var bytes=pdf.Generate(organization.Name,organization.Currency,"Отчёт по взаиморасчётам",$"По состоянию на {date:dd.MM.yyyy} · {count} записей",lines,balances:false);
            await audit.Add(ctx.UserId(),"pdf_exported","settlement_report",null,new{counterpartyId,direction,status,q,overdueOnly,date,count});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.File(bytes,"application/pdf","Faiza-Settlements.pdf");
        }).RequireAuthorization(new AuthorizeAttribute{Roles="admin,accountant,manager,auditor"});
        group.MapGet("/settlements",async(CashDb db,HttpContext ctx,ICashboxAccess access,long? counterpartyId,string? direction,string? status,string? q,bool? overdueOnly,DateOnly? asOf,int? page,int? pageSize)=>
        {
            var date=asOf??Local.Today;
            if(direction is not (null or "receivable" or "payable")||status is not (null or "open" or "partially_paid" or "paid")||q?.Length>160||q?.Contains('\0')==true||date==DateOnly.MinValue)
                throw new BusinessException("Некорректный фильтр отчёта по задолженностям.",400);
            var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
            var query=db.Settlements.AsNoTracking().Where(x=>(ctx.User.IsInRole("admin")||(x.CashboxId.HasValue&&allowed.Contains(x.CashboxId.Value))||x.Allocations.Any(a=>allowed.Contains(a.Operation.CashboxId)))&&
                (!counterpartyId.HasValue||x.CounterpartyId==counterpartyId)&&
                (direction==null||x.Direction==direction)&&
                (status==null||x.Status==status)&&
                (!overdueOnly.GetValueOrDefault()||x.Status!="paid"&&x.DueDate<date)&&
                (q==null||(x.CounterpartyNameSnapshot!=null&&x.CounterpartyNameSnapshot.Contains(q))||x.Counterparty.Name.Contains(q)||x.BasisReference.Contains(q)));
            var totals=await query.GroupBy(_=>1).Select(g=>new
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
            var total=await query.CountAsync(ctx.RequestAborted);var currentPage=Math.Clamp(page??1,1,1000000);var size=Math.Clamp(pageSize??100,1,500);
            var items=await query.OrderBy(x=>x.DueDate).ThenBy(x=>x.Id).Skip((currentPage-1)*size).Take(size)
                .Select(x=>new{x.Id,x.CounterpartyId,cashboxId=x.CashboxId,cashbox=x.Cashbox==null?null:x.Cashbox.Name,counterparty=x.CounterpartyNameSnapshot??x.Counterparty.Name,x.Direction,x.BasisType,x.BasisReference,x.TotalAmount,x.PaidAmount,remaining=x.TotalAmount-x.PaidAmount,x.Status,x.DueDate,overdue=x.Status!="paid"&&x.DueDate<date})
                .ToListAsync(ctx.RequestAborted);
            return Results.Ok(new{asOf=date,page=currentPage,pageSize=size,total,summary=totals,items});
        }).RequireAuthorization(new AuthorizeAttribute{Roles="admin,accountant,manager,auditor"});
        group.MapGet("/cashbook",async([AsParameters]ReportFilters f,HttpContext ctx,CashDb db,ICashboxAccess access)=>
        {
            Validate(f);await access.Require(ctx.UserId(),f.CashboxId,ctx.RequestAborted);
            if(f.Type!=null||f.CategoryId.HasValue||f.CounterpartyId.HasValue||!string.IsNullOrEmpty(f.Q)||f.Status is "all" or "draft")throw new BusinessException("Кассовая книга включает все проведённые документы за период. Для дополнительных фильтров используйте журнал операций.",400);
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var opening=await Opening(db,f.CashboxId,f.From);
            var activation = await db.Openings.AsNoTracking().SingleOrDefaultAsync(x =>
                x.CashboxId == f.CashboxId && x.Date > f.From && x.Date <= f.To, ctx.RequestAborted);
            var activationDate = activation?.Date ?? DateOnly.MaxValue;
            var activationAmount = activation?.Amount ?? 0m;
            var ledger=Filtered(db,f,true);
            var total=await ledger.CountAsync(ctx.RequestAborted);var page=Math.Clamp(f.Page??1,1,1000000);var size=Math.Clamp(f.PageSize??15,1,100);
            var rows=await ledger.OrderBy(x=>x.Date).ThenBy(x=>x.PostingSeq).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).Select(x=>new {
                x.Id,x.Date,x.DocumentNumber,x.Description,counterparty=x.Counterparty==null?null:x.CounterpartyNameSnapshot??x.Counterparty.Name,x.Type,x.Amount,
                balance=opening+(x.Date>=activationDate?activationAmount:0m)+db.Operations.Where(p=>p.CashboxId==f.CashboxId&&p.Status=="posted"&&p.Date>=f.From&&(p.Date<x.Date || p.Date==x.Date&&(p.PostingSeq<x.PostingSeq || p.PostingSeq==x.PostingSeq&&p.Id<=x.Id))).Sum(p=>p.Type=="income"?p.Amount:-p.Amount)
            }).ToListAsync(ctx.RequestAborted);
            var (income,expense)=await Totals(ledger,ctx.RequestAborted);
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.Ok(new {page,pageSize=size,total,opening,income,expense,activationAmount,closing=opening+activationAmount+income-expense,items=rows});
        });
        group.MapGet("/cash.csv",async([AsParameters]ReportFilters f,CashDb db,HttpContext ctx,Audit audit,ICashboxAccess access)=>
        {
            Validate(f);await access.Require(ctx.UserId(),f.CashboxId,ctx.RequestAborted);
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var query=Filtered(db,f);var count=await query.CountAsync(ctx.RequestAborted);
            if(count>MaxExportRows)throw new BusinessException($"Экспорт ограничен {MaxExportRows} строками. Уточните фильтр периода.",413);
            var (income,expense)=await Totals(query,ctx.RequestAborted);
            var book=IsBook(f);var balance=book?await Opening(db,f.CashboxId,f.From):0m;
            ctx.Response.ContentType="text/csv; charset=utf-8";
            ctx.Response.Headers.ContentDisposition="attachment; filename=\"Faiza-Cash.csv\"";
            await ctx.Response.StartAsync(ctx.RequestAborted);
            await using var writer=new StreamWriter(ctx.Response.Body,new UTF8Encoding(true),4096,leaveOpen:true);
            await writer.WriteAsync("Дата;Номер;Тип;Содержание;Контрагент;Категория;Статус;Приход;Расход"+(book?";Остаток после операции":"")+"\r\n");
            if(book)await writer.WriteAsync(";;;"+Cell("Остаток на начало")+";;;;;;"+balance.ToString("0.00",CultureInfo.InvariantCulture)+"\r\n");
            var rows=Ordered(query,f.Sort).Include(x=>x.Counterparty).Include(x=>x.Category).AsAsyncEnumerable();
            await foreach(var row in rows.WithCancellation(ctx.RequestAborted))
            {
                var line=string.Join(';',Cell(row.Date.ToString("dd.MM.yyyy")),Cell(row.DocumentNumber??$"Черновик №{row.Id}"),Cell(row.Type=="income"?"Приход":"Расход"),Cell(row.Description),Cell(row.CounterpartyNameSnapshot??row.Counterparty?.Name),Cell(row.CategoryNameSnapshot??row.Category.Name),Cell(row.Status=="posted"?"Проведено":"Черновик"),row.Type=="income"?row.Amount.ToString("0.00",CultureInfo.InvariantCulture):"",row.Type=="expense"?row.Amount.ToString("0.00",CultureInfo.InvariantCulture):"");
                if(book){balance+=Money.Signed(row.Type,row.Amount);line+=";"+balance.ToString("0.00",CultureInfo.InvariantCulture);}
                await writer.WriteAsync(line+"\r\n");
            }
            if(book)await writer.WriteAsync(";;;"+Cell("Итоги периода / остаток на конец")+";;;;"+income.ToString("0.00",CultureInfo.InvariantCulture)+";"+expense.ToString("0.00",CultureInfo.InvariantCulture)+";"+balance.ToString("0.00",CultureInfo.InvariantCulture)+"\r\n");
            await writer.FlushAsync(ctx.RequestAborted);
            await audit.Add(ctx.UserId(),"report_exported","report",null,new{f.CashboxId,f.From,f.To,count});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.Empty;
        });
        group.MapGet("/cash.pdf",async([AsParameters]ReportFilters f,CashDb db,HttpContext ctx,Audit audit,IPdfReport pdf,ICashboxAccess access)=>
        {
            Validate(f);await access.Require(ctx.UserId(),f.CashboxId,ctx.RequestAborted);
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ctx.RequestAborted);
            var query=Filtered(db,f);var count=await query.CountAsync(ctx.RequestAborted);
            if(count>MaxExportRows)throw new BusinessException($"Экспорт ограничен {MaxExportRows} строками. Уточните фильтр периода.",413);
            var org=await db.Organizations.SingleAsync(ctx.RequestAborted);var box=await db.Cashboxes.SingleOrDefaultAsync(x=>x.Id==f.CashboxId,ctx.RequestAborted)??throw new KeyNotFoundException();
            var rows=await Ordered(query,f.Sort).Include(x=>x.Counterparty).ToListAsync(ctx.RequestAborted);
            var isBook=IsBook(f);
            var opening=isBook?await Opening(db,f.CashboxId,f.From):0m;var balance=opening;
            var lines=(isBook?rows.OrderBy(x=>x.Date).ThenBy(x=>x.PostingSeq).ThenBy(x=>x.Id):rows.AsEnumerable()).Select(x=>{balance+=Money.Signed(x.Type,x.Amount);return new ReportLine(x.DocumentNumber??$"Черновик №{x.Id}",x.Date,x.Description,x.Type,x.Amount,x.CounterpartyNameSnapshot??x.Counterparty?.Name,isBook?balance:null,x.Status);}).ToList();
            var bytes=pdf.Generate(org.Name,org.Currency,isBook?"Учебная кассовая книга":"Отфильтрованный журнал операций",$"{box.Name} · {f.From:dd.MM.yyyy} — {f.To:dd.MM.yyyy} · Все найденные записи",lines,opening,balances:isBook);
            await audit.Add(ctx.UserId(),"pdf_exported","report",null,new{f.CashboxId,f.From,f.To,count});
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.File(bytes,"application/pdf","Faiza-Cash.pdf");
        });
    }
    private static void Validate(ReportFilters f)
    {
        if(f.CashboxId<=0 || f.From==DateOnly.MinValue || f.From>f.To || f.To.DayNumber-f.From.DayNumber>3660)throw new BusinessException("Укажите кассу и корректный период (не более 10 лет).",400);
        if(f.Type is not (null or "income" or "expense") || f.Status is not (null or "all" or "draft" or "posted") || f.Sort is not (null or "amount_asc" or "amount_desc" or "date_asc" or "date_desc") || f.Q?.Length>1000 || f.Q?.Contains('\0')==true)
            throw new BusinessException("Некорректный фильтр операций.",400);
    }
    private static void ValidatePeriod(DateOnly from,DateOnly to)
    {
        if(from==DateOnly.MinValue||from>to||to.DayNumber-from.DayNumber>3660)
            throw new BusinessException("Укажите корректный период (не более 10 лет).",400);
    }
    private static IQueryable<CashOperation> Filtered(CashDb db,ReportFilters f,bool posted=false)=>db.Operations.AsNoTracking().Where(x=>x.CashboxId==f.CashboxId&&x.Date>=f.From&&x.Date<=f.To&&(posted?x.Status=="posted":f.Status=="all"||x.Status==(f.Status??"posted"))&&(f.Type==null||x.Type==f.Type)&&(!f.CategoryId.HasValue||x.CategoryId==f.CategoryId)&&(!f.CounterpartyId.HasValue||x.CounterpartyId==f.CounterpartyId)&&(string.IsNullOrEmpty(f.Q)||x.Description.Contains(f.Q)||(x.DocumentNumber??"").Contains(f.Q)));
    private static bool IsBook(ReportFilters f)=>f.Sort==null&&f.Type==null&&f.CategoryId==null&&f.CounterpartyId==null&&string.IsNullOrEmpty(f.Q)&&f.Status is null or "posted";
    private static IOrderedQueryable<CashOperation> Ordered(IQueryable<CashOperation> rows,string? sort)=>sort switch{"amount_asc"=>rows.OrderBy(x=>x.Amount).ThenBy(x=>x.Id),"amount_desc"=>rows.OrderByDescending(x=>x.Amount).ThenBy(x=>x.Id),"date_desc"=>rows.OrderByDescending(x=>x.Date).ThenByDescending(x=>x.Id),"date_asc"=>rows.OrderBy(x=>x.Date).ThenBy(x=>x.Id),_=>rows.OrderBy(x=>x.Date).ThenBy(x=>x.PostingSeq).ThenBy(x=>x.Id)};
    private static async Task<decimal> Opening(CashDb db,long box,DateOnly from)
    {
        if(!await db.Cashboxes.AnyAsync(x=>x.Id==box))throw new KeyNotFoundException();
        var initial=await db.Openings.Where(x=>x.CashboxId==box&&x.Date<=from).SumAsync(x=>x.Amount);
        return initial+await db.Operations.Where(x=>x.CashboxId==box&&x.Status=="posted"&&x.Date<from).SumAsync(x=>x.Type=="income"?x.Amount:-x.Amount);
    }
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
