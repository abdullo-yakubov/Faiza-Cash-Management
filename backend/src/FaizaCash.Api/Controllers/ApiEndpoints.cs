using FaizaCash.Domain;
using FaizaCash.Application;
using FaizaCash.Infrastructure;
using FaizaCash.Infrastructure.Services;
using FaizaCash.Api;
using static FaizaCash.Api.RoleNames;
using static FaizaCash.Api.EndpointHelpers;
using static FaizaCash.Api.OperationsValidators;
using static FaizaCash.Api.BackupResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;


namespace FaizaCash.Api;
public static class ApiEndpoints {
private sealed record AttentionItem(string Code,string Severity,string Title,string Message,decimal? Amount,string Href,long? ObjectId);
private sealed record NotificationItem(string Key,string Priority,DateOnly Date,string Reason,string ObjectType,long? ObjectId,string ActionHref);
public static void MapCashApi(this WebApplication app,IConfiguration configuration) {
app.MapReports(); app.MapAudit(); app.MapDirectories();
app.MapBackupsEndpoints();
app.MapAuthEndpoints();
app.MapUsersEndpoints();
app.MapOrganizationEndpoints();
app.MapGet("/health", async (CashDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok", utc = DateTime.UtcNow }) : Results.Problem("База данных недоступна.", statusCode: 503));



app.MapGet("/api/shifts", [Authorize] async (long cashboxId, DateOnly? date, HttpContext ctx, CashDb db, ICashboxAccess access) =>
{
    await access.Require(ctx.UserId(), cashboxId, ctx.RequestAborted);
    var query=db.Shifts.AsNoTracking().Where(x=>x.CashboxId==cashboxId);
    if(date.HasValue) query=query.Where(x=>x.Date==date.Value);
    return Results.Ok(await query.OrderByDescending(x=>x.Date).Take(30).ToListAsync(ctx.RequestAborted));
});
app.MapPost("/api/shifts/open", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (ShiftOpenInput x, HttpContext ctx, CashDb db, ICashboxAccess access, IBalanceService balances, Audit audit) =>
{
    await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted);
    if(x.OpeningBalance<0 || x.Date==DateOnly.MinValue || x.Date>Local.Today || decimal.Round(x.OpeningBalance,2)!=x.OpeningBalance) return Results.Problem("Укажите неотрицательный остаток и дату не позже сегодняшней.",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var box=await db.Cashboxes.FromSqlInterpolated($"SELECT * FROM \"Cashboxes\" WHERE \"Id\"={x.CashboxId} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
    if(box is null || !box.Active) return Results.NotFound();
    if(await db.Shifts.AnyAsync(s=>s.CashboxId==x.CashboxId&&s.Status=="open",ctx.RequestAborted)) return Results.Problem("У кассы уже есть открытая смена.",statusCode:409);
    if(await db.Openings.AnyAsync(o=>o.CashboxId==x.CashboxId&&o.Date>x.Date,ctx.RequestAborted)) return Results.Problem("Касса начинает учёт позже выбранной даты.",statusCode:400);
    // D-03: the declared opening balance must equal the ledger balance; a mismatch is reported, not silently stored.
    var expected=await balances.BalanceAt(x.CashboxId,x.Date,ctx.RequestAborted);
    if(x.OpeningBalance!=expected)
        return Results.Problem($"Начальный остаток смены должен совпадать с расчётным остатком кассы: {expected.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)}. Пересчитайте наличные; расхождение фиксируется при закрытии смены.",statusCode:409,extensions:new Dictionary<string,object?>{["expectedBalance"]=expected.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)});
    var shift=new CashShift{CashboxId=x.CashboxId,Date=x.Date,OpeningBalance=x.OpeningBalance,OpenedBy=ctx.UserId()};
    db.Shifts.Add(shift); await db.SaveChangesAsync(ctx.RequestAborted);
    await audit.Add(ctx.UserId(),"shift_opened","shift",shift.Id,new{shift.CashboxId,shift.Date,shift.OpeningBalance});
    await tx.CommitAsync(ctx.RequestAborted); return Results.Created($"/api/shifts/{shift.Id}",shift);
});
app.MapPost("/api/shifts/{id:long}/close", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id, ShiftCloseInput x, HttpContext ctx, CashDb db, ICashboxAccess access, IBalanceService balances, Audit audit) =>
{
    if(x.ClosingBalance<0||decimal.Round(x.ClosingBalance,2)!=x.ClosingBalance)return Results.Problem("Фактический остаток должен быть неотрицательным, с точностью до дирама.",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var shift=await db.Shifts.FromSqlInterpolated($"SELECT * FROM \"Shifts\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
    if(shift is null) return Results.NotFound();
    await access.Require(ctx.UserId(),shift.CashboxId,ctx.RequestAborted);
    if(!ShiftRules.CanClose(shift.Status,shift.OpeningBalance,x.ClosingBalance)) return Results.Problem("Смена уже закрыта.",statusCode:409);
    var expected=await balances.BalanceAt(shift.CashboxId,shift.Date,ctx.RequestAborted);
    shift.ClosingBalance=x.ClosingBalance; shift.ClosingDifference=ShiftRules.ClosingDifference(expected,x.ClosingBalance); shift.Status="closed"; shift.ClosedBy=ctx.UserId(); shift.ClosedAt=DateTime.UtcNow;
    await db.SaveChangesAsync(ctx.RequestAborted);
    await audit.Add(ctx.UserId(),"shift_closed","shift",shift.Id,new{shift.CashboxId,shift.Date,expected,shift.ClosingBalance,shift.ClosingDifference});
    await tx.CommitAsync(ctx.RequestAborted); return Results.Ok(shift);
});

app.MapPost("/api/transfers", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (CashTransferInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
{
    var description=x.Description?.Trim();
    if (x.SourceCashboxId == x.TargetCashboxId || !Money.IsValid(x.Amount) || x.Date==DateOnly.MinValue || x.Date > Local.Today || x.RequestKey == Guid.Empty || string.IsNullOrWhiteSpace(description) || description.Length > 500 || description.Contains('\0'))
        return Results.BadRequest();
    await access.Require(ctx.UserId(), x.SourceCashboxId, ctx.RequestAborted); await access.Require(ctx.UserId(), x.TargetCashboxId, ctx.RequestAborted);
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ctx.RequestAborted);
    var existing = await db.Transfers.AsNoTracking().SingleOrDefaultAsync(t=>t.RequestKey==x.RequestKey,ctx.RequestAborted);
    if(existing is not null)
    {
        if(existing.CreatedBy!=ctx.UserId()||existing.SourceCashboxId!=x.SourceCashboxId||existing.TargetCashboxId!=x.TargetCashboxId||existing.Date!=x.Date||existing.Amount!=x.Amount||existing.Description!=description)
            return Results.Conflict();
        return Results.Ok(TransferView(existing));
    }
    var ids = new[]{x.SourceCashboxId,x.TargetCashboxId}.OrderBy(x=>x).ToArray();
    var boxes = await db.Cashboxes.FromSqlInterpolated($"SELECT * FROM \"Cashboxes\" WHERE \"Id\" IN ({ids[0]},{ids[1]}) ORDER BY \"Id\" FOR UPDATE").ToListAsync(ctx.RequestAborted);
    var source=boxes.SingleOrDefault(b=>b.Id==x.SourceCashboxId); var target=boxes.SingleOrDefault(b=>b.Id==x.TargetCashboxId);
    if(source is null||target is null||!source.Active||!target.Active)return Results.NotFound();
    var openings=await db.Openings.Where(o=>ids.Contains(o.CashboxId)).ToDictionaryAsync(o=>o.CashboxId,o=>o,ctx.RequestAborted);
    if(openings.TryGetValue(source.Id,out var sourceOpening)&&x.Date<sourceOpening.Date||openings.TryGetValue(target.Id,out var targetOpening)&&x.Date<targetOpening.Date)return Results.BadRequest();
    var shifts=await db.Shifts.FromSqlInterpolated($"SELECT * FROM \"Shifts\" WHERE \"CashboxId\" IN ({ids[0]},{ids[1]}) AND \"Date\"={x.Date} ORDER BY \"CashboxId\" FOR UPDATE").ToListAsync(ctx.RequestAborted);
    var sourceShift=shifts.SingleOrDefault(s=>s.CashboxId==source.Id); var targetShift=shifts.SingleOrDefault(s=>s.CashboxId==target.Id);
    if(sourceShift is null||sourceShift.Status!="open"||targetShift is null||targetShift.Status!="open")return Results.Conflict();
    var ledger=await db.Operations.Where(o=>ids.Contains(o.CashboxId)&&o.Status=="posted").Select(o=>new {o.CashboxId,Item=new PostingItem(o.Date,o.PostingSeq,o.Type,o.Amount)}).ToListAsync(ctx.RequestAborted);
    var sourceLedger=ledger.Where(x=>x.CashboxId==source.Id).Select(x=>x.Item).ToArray();
    var targetLedger=ledger.Where(x=>x.CashboxId==target.Id).Select(x=>x.Item).ToArray();
    var sourceOpeningAmount=sourceOpening?.Amount??0m; var targetOpeningAmount=targetOpening?.Amount??0m;
    var sourceSeq=(sourceLedger.Max(o=>o.PostingSeq)??0)+1;
    var targetSeq=(targetLedger.Max(o=>o.PostingSeq)??0)+1;
    var sourceBalance=BalanceRules.BalanceAt(sourceOpeningAmount,sourceLedger,x.Date);
    var targetBalance=BalanceRules.BalanceAt(targetOpeningAmount,targetLedger,x.Date);
    if(sourceBalance<x.Amount||!BalanceRules.CanPost(sourceOpeningAmount,sourceLedger,new(x.Date,sourceSeq,"expense",x.Amount)))return Results.Conflict();
    if(target.BalanceLimit.HasValue&&targetBalance+x.Amount>target.BalanceLimit.Value||targetBalance+x.Amount>9999999999999999.99m)return Results.Conflict();
    var expenseCategory=await db.Categories.Where(c=>c.Active&&c.Type=="expense").OrderBy(c=>c.Id).FirstOrDefaultAsync(ctx.RequestAborted);
    var incomeCategory=await db.Categories.Where(c=>c.Active&&c.Type=="income").OrderBy(c=>c.Id).FirstOrDefaultAsync(ctx.RequestAborted);
    if(expenseCategory is null||incomeCategory is null)return Results.Conflict();
    var transfer=new CashTransfer{SourceCashboxId=source.Id,TargetCashboxId=target.Id,Date=x.Date,Amount=x.Amount,Description=description,RequestKey=x.RequestKey,CreatedBy=ctx.UserId()}; db.Transfers.Add(transfer); await db.SaveChangesAsync(ctx.RequestAborted);
    var year=(short)x.Date.Year; var sourceCounter=await db.Sequences.SingleOrDefaultAsync(s=>s.CashboxId==source.Id&&s.Type=="expense"&&s.Year==year,ctx.RequestAborted); var targetCounter=await db.Sequences.SingleOrDefaultAsync(s=>s.CashboxId==target.Id&&s.Type=="income"&&s.Year==year,ctx.RequestAborted); if(sourceCounter is null){sourceCounter=new DocumentSequence{CashboxId=source.Id,Type="expense",Year=year};db.Sequences.Add(sourceCounter);} if(targetCounter is null){targetCounter=new DocumentSequence{CashboxId=target.Id,Type="income",Year=year};db.Sequences.Add(targetCounter);} sourceCounter.LastNumber++; targetCounter.LastNumber++;
    var sourceOp=new CashOperation{CashboxId=source.Id,Date=x.Date,Type="expense",Status="posted",Amount=x.Amount,Description="Межкассовый перевод: "+transfer.Description,CategoryId=expenseCategory.Id,DocumentNumber=$"ПЕРЕВОД-РКО-{source.Code}-{year}-{sourceCounter.LastNumber:D6}",PostingSeq=sourceSeq,ShiftId=sourceShift.Id,TransferId=transfer.Id,RequestKey=Guid.NewGuid(),CreatedBy=ctx.UserId(),PostedBy=ctx.UserId(),PostedAt=DateTime.UtcNow};
    var targetOp=new CashOperation{CashboxId=target.Id,Date=x.Date,Type="income",Status="posted",Amount=x.Amount,Description="Межкассовый перевод: "+transfer.Description,CategoryId=incomeCategory.Id,DocumentNumber=$"ПЕРЕВОД-ПКО-{target.Code}-{year}-{targetCounter.LastNumber:D6}",PostingSeq=targetSeq,ShiftId=targetShift.Id,TransferId=transfer.Id,RequestKey=Guid.NewGuid(),CreatedBy=ctx.UserId(),PostedBy=ctx.UserId(),PostedAt=DateTime.UtcNow};
    await CaptureDocumentSnapshot(db,sourceOp,ctx.RequestAborted);await CaptureDocumentSnapshot(db,targetOp,ctx.RequestAborted);
    db.Operations.AddRange(sourceOp,targetOp); await db.SaveChangesAsync(ctx.RequestAborted); transfer.SourceOperationId=sourceOp.Id; transfer.TargetOperationId=targetOp.Id; await db.SaveChangesAsync(ctx.RequestAborted); await audit.Add(ctx.UserId(),"cash_transfer_posted","cash_transfer",transfer.Id,new{transfer.SourceCashboxId,transfer.TargetCashboxId,transfer.Amount}); await tx.CommitAsync(ctx.RequestAborted); return Results.Created($"/api/transfers/{transfer.Id}",TransferView(transfer));
});
app.MapGet("/api/transfers", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,long? cashboxId,DateOnly? from,DateOnly? to,int page=1,int pageSize=25) =>
{
    if(from.HasValue&&to.HasValue&&from>to)return Results.BadRequest();
    var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
    if(!ctx.User.IsInRole(Admin)&&!cashboxId.HasValue)throw new BusinessException("Выберите доступную кассу.",400);
    if(cashboxId.HasValue)await access.Require(ctx.UserId(),cashboxId.Value,ctx.RequestAborted);
    var query=db.Transfers.AsNoTracking().Where(t=>allowed.Contains(t.SourceCashboxId)&&allowed.Contains(t.TargetCashboxId)&&(!cashboxId.HasValue||t.SourceCashboxId==cashboxId||t.TargetCashboxId==cashboxId)&&(!from.HasValue||t.Date>=from)&&(!to.HasValue||t.Date<=to));
    return await Paged(query.OrderByDescending(t=>t.Date).ThenByDescending(t=>t.Id),page,pageSize,TransferView);
});
app.MapGet("/api/transfers/{id:long}", [Authorize] async (long id,HttpContext ctx,CashDb db,ICashboxAccess access) =>
{
    var transfer=await db.Transfers.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==id,ctx.RequestAborted);
    if(transfer is null)return Results.NotFound();
    await access.Require(ctx.UserId(),transfer.SourceCashboxId,ctx.RequestAborted);
    await access.Require(ctx.UserId(),transfer.TargetCashboxId,ctx.RequestAborted);
    return Results.Ok(TransferView(transfer));
});

app.MapGet("/api/categories", [Authorize] async (HttpContext ctx, CashDb db, string? type, string? q, int? page, int? pageSize) =>
{
    if(type is not null&&type is not ("income" or "expense"))return Results.Problem("Недопустимое направление категории.",statusCode:400);
    if(q?.Length>160||q?.Contains('\0')==true)return Results.Problem("Некорректный поисковый запрос.",statusCode:400);
    return await ListPage(ctx,db.Categories.AsNoTracking().Where(x=>(type==null||x.Type==type)&&(q==null||x.Name.Contains(q))).OrderBy(x=>x.Name).ThenBy(x=>x.Id),page,pageSize,ctx.RequestAborted);
});
app.MapGet("/api/counterparties", [Authorize] async (HttpContext ctx, CashDb db, string? q, int? page, int? pageSize) =>
{
    if(q?.Length>160||q?.Contains('\0')==true)return Results.Problem("Некорректный поисковый запрос.",statusCode:400);
    return await ListPage(ctx,db.Counterparties.AsNoTracking().Where(x=>q==null||x.Name.Contains(q)).OrderBy(x=>x.Name).ThenBy(x=>x.Id),page,pageSize,ctx.RequestAborted);
});
app.MapGet("/api/settlements", [Authorize(Roles = Admin + "," + Accountant + "," + Manager + "," + Auditor)] async (CashDb db, HttpContext ctx, ICashboxAccess access, long? counterpartyId, Guid? representativeId, string? direction, string? status, string? q, bool? overdueOnly, DateOnly? asOf, int? page, int? pageSize, CancellationToken ct) =>
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
app.MapGet("/api/representatives", [Authorize(Roles = Admin + "," + Accountant + "," + Manager + "," + Auditor)] async (CashDb db, CancellationToken ct) =>
    Results.Ok(await db.Users.AsNoTracking().Where(u=>u.Active).OrderBy(u=>u.FullName).Select(u=>new{u.Id,u.FullName}).Take(MaxListSize).ToListAsync(ct)));
app.MapPost("/api/settlements", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (SettlementInput x, CashDb db, Audit audit, HttpContext ctx, ICashboxAccess access) =>
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
app.MapGet("/api/settlements/{id:long}/history", [Authorize] async (long id,CashDb db, HttpContext ctx, ICashboxAccess access) => { var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted); var visible=await db.Settlements.AnyAsync(s=>s.Id==id&&(ctx.User.IsInRole(Admin)||(s.CashboxId.HasValue&&allowed.Contains(s.CashboxId.Value))||s.Allocations.Any(a=>allowed.Contains(a.Operation.CashboxId))),ctx.RequestAborted); if(!visible)return Results.NotFound(); return Results.Ok(await db.PaymentAllocations.AsNoTracking().Where(x=>x.SettlementId==id&&allowed.Contains(x.Operation.CashboxId)).Include(x=>x.Operation).OrderBy(x=>x.CreatedAt).Select(x=>new{x.Id,x.SettlementId,x.OperationId,x.Amount,x.CreatedAt,operationType=x.Operation.Type,cashboxId=x.Operation.CashboxId,documentNumber=x.Operation.DocumentNumber}).ToListAsync()); });
app.MapGet("/api/representative-receipts", [Authorize] async (CashDb db, Guid? representativeId, string? status, int? page, int? pageSize, HttpContext ctx, ICashboxAccess access) =>
{
    if(status is not (null or "collected" or "handed_over" or "accepted" or "cancelled"))return Results.Problem("Недопустимый статус квитанции.",statusCode:400);
    var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
    var query=db.RepresentativeReceipts.AsNoTracking().Where(x=>allowed.Contains(x.CashboxId)&&(!representativeId.HasValue||x.RepresentativeId==representativeId)&&(status==null||x.Status==status)).OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id)
        .Select(x=>new{x.Id,x.RepresentativeId,representative=db.Users.Where(u=>u.Id==x.RepresentativeId).Select(u=>u.FullName).FirstOrDefault(),x.CounterpartyId,counterparty=x.Counterparty.Name,x.BasisType,x.BasisReference,x.Amount,x.CashboxId,x.Status,x.RequestKey,x.CreatedAt,x.HandedOverAt,x.AcceptedBy,x.AcceptedAt});
    return await ListPage(ctx,query,page,pageSize,ctx.RequestAborted);
});
app.MapPost("/api/representative-receipts/{id:long}/cancel", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (long id, CancelInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
{
    var reason=x.Reason?.Trim();
    if(string.IsNullOrWhiteSpace(reason)||reason.Length>500||reason.Contains('\0'))return Results.Problem("Укажите причину отмены (до 500 символов).",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var r=await db.RepresentativeReceipts.FromSqlInterpolated($"SELECT * FROM \"RepresentativeReceipts\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
    if(r is null)return Results.NotFound();
    await access.Require(ctx.UserId(),r.CashboxId,ctx.RequestAborted);
    if(r.Status is not ("collected" or "handed_over"))return Results.Problem("Отменить можно только квитанцию, деньги по которой ещё не приняты кассой.",statusCode:409);
    var before=r.Status;r.Status="cancelled";await db.SaveChangesAsync(ctx.RequestAborted);
    await audit.Add(ctx.UserId(),"representative_receipt_cancelled","representative_receipt",id,new{before,reason});
    await tx.CommitAsync(ctx.RequestAborted);
    return Results.Ok(r);
});
app.MapPost("/api/settlements/{id:long}/cancel", [Authorize(Roles = Admin + "," + Accountant)] async (long id, CancelInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
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
app.MapPost("/api/representative-receipts", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (RepresentativeReceiptInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
{
    // D-05: exact money, bounded reference, mandatory idempotency key, transactional create.
    var reference=x.BasisReference?.Trim();
    if(!Money.IsValid(x.Amount)||x.RequestKey==Guid.Empty||string.IsNullOrWhiteSpace(reference)||reference.Length>120||reference.Contains('\0')||x.BasisType is not ("order" or "delivery" or "return" or "manual"))
        return Results.Problem("Проверьте сумму (до двух знаков), основание (до 120 символов) и ключ запроса.",statusCode:400);
    await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted);
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var existing=await db.RepresentativeReceipts.AsNoTracking().SingleOrDefaultAsync(r=>r.RequestKey==x.RequestKey,ctx.RequestAborted);
    if(existing is not null)
    {
        // Never disclose a receipt created by someone else or for a different payload.
        if(existing.CreatedBy!=ctx.UserId()||existing.RepresentativeId!=x.RepresentativeId||existing.CounterpartyId!=x.CounterpartyId||existing.CashboxId!=x.CashboxId||existing.BasisType!=x.BasisType||existing.BasisReference!=reference||existing.Amount!=x.Amount)
            return Results.Problem("Ключ запроса уже использован для другой квитанции.",statusCode:409);
        return Results.Ok(existing);
    }
    if(!await db.Cashboxes.AnyAsync(c=>c.Id==x.CashboxId&&c.Active,ctx.RequestAborted))return Results.Problem("Касса недоступна.",statusCode:400);
    if(!await db.Users.AnyAsync(u=>u.Id==x.RepresentativeId&&u.Active,ctx.RequestAborted))return Results.Problem("Представитель не найден или отключён.",statusCode:400);
    if(!await db.Counterparties.AnyAsync(c=>c.Id==x.CounterpartyId&&c.Active,ctx.RequestAborted))return Results.Problem("Контрагент не найден.",statusCode:400);
    var r=new RepresentativeReceipt{RepresentativeId=x.RepresentativeId,CounterpartyId=x.CounterpartyId,BasisType=x.BasisType!,BasisReference=reference,Amount=x.Amount,CashboxId=x.CashboxId,RequestKey=x.RequestKey,CreatedBy=ctx.UserId()};
    db.RepresentativeReceipts.Add(r); await db.SaveChangesAsync(ctx.RequestAborted);
    await audit.Add(ctx.UserId(),"representative_receipt_created","representative_receipt",r.Id,new{x.RepresentativeId,x.CounterpartyId,x.CashboxId,x.Amount});
    await tx.CommitAsync(ctx.RequestAborted);
    return Results.Created($"/api/representative-receipts/{r.Id}",r);
});
app.MapPost("/api/representative-receipts/{id:long}/handover", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (long id, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) => { var r=await db.RepresentativeReceipts.FindAsync(id); if(r is null)return Results.NotFound(); await access.Require(ctx.UserId(),r.CashboxId,ctx.RequestAborted); if(r.Status!="collected")return Results.Conflict(); r.Status="handed_over";r.HandedOverAt=DateTime.UtcNow;await db.SaveChangesAsync();await audit.Add(ctx.UserId(),"representative_receipt_handed_over","representative_receipt",id,new{});return Results.Ok(r); });
app.MapPost("/api/representative-receipts/{id:long}/accept", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) => { var r=await db.RepresentativeReceipts.FindAsync(id); if(r is null)return Results.NotFound(); await access.Require(ctx.UserId(),r.CashboxId,ctx.RequestAborted); if(r.Status!="handed_over")return Results.Conflict(); r.Status="accepted";r.AcceptedAt=DateTime.UtcNow;r.AcceptedBy=ctx.UserId();await db.SaveChangesAsync();await audit.Add(ctx.UserId(),"representative_receipt_accepted","representative_receipt",id,new{});return Results.Ok(r); });
app.MapPost("/api/settlements/{id:long}/allocate", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (long id, PaymentAllocationInput x, HttpContext ctx, CashDb db, Audit audit) =>
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
app.MapGet("/api/document-types", [Authorize] async (CashDb db) => Results.Ok(await db.DocumentTypes.OrderBy(x => x.Name).ToListAsync()));
app.MapPost("/api/document-types", [Authorize(Roles = Admin + "," + Accountant)] async (DocumentTypeInput x, CashDb db) =>
{
    var code=x.Code?.Trim().ToUpperInvariant();var name=x.Name?.Trim();
    if(string.IsNullOrEmpty(code)||code.Length>24||code.Contains('\0')||string.IsNullOrEmpty(name)||name.Length>160||name.Contains('\0'))
        return Results.Problem("Укажите код длиной до 24 символов и название длиной до 160 символов.",statusCode:400);
    db.DocumentTypes.Add(new(){Code=code,Name=name});
    try { await db.SaveChangesAsync(); }
    catch(DbUpdateException error) when(error.InnerException is PostgresException { SqlState:PostgresErrorCodes.UniqueViolation,ConstraintName:"IX_DocumentTypes_NormalizedCode" })
    {
        return Results.Problem("Код вида документа уже существует.",statusCode:409);
    }
    return Results.NoContent();
});

app.MapGet("/api/operations", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,long? cashboxId, DateOnly? from, DateOnly? to, string? type, string? status, long? categoryId, long? counterpartyId, string? q, string? sort, int page = 1, int pageSize = 25) =>
{
    if(type is not (null or "income" or "expense")||status is not (null or "all" or "draft" or "posted")||sort is not (null or "amount_asc" or "amount_desc" or "date_asc" or "date_desc")||q?.Length>1000||q?.Contains('\0')==true)
        throw new BusinessException("Некорректный фильтр операций.",400);
    var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
    if(!ctx.User.IsInRole(Admin)&&!cashboxId.HasValue)throw new BusinessException("Выберите доступную кассу.",400);
    if(cashboxId.HasValue)await access.Require(ctx.UserId(),cashboxId.Value,ctx.RequestAborted);
    var rows = db.Operations.Include(x => x.Category).Include(x => x.Counterparty).Include(x => x.Author).Where(x => allowed.Contains(x.CashboxId) &&
        (!cashboxId.HasValue || x.CashboxId == cashboxId) && (!from.HasValue || x.Date >= from) && (!to.HasValue || x.Date <= to) &&
        (type == null || x.Type == type) && (status == null || status == "all" || x.Status == status) && (!categoryId.HasValue || x.CategoryId == categoryId) &&
        (!counterpartyId.HasValue || x.CounterpartyId == counterpartyId) && (q == null || x.Description.Contains(q) || (x.DocumentNumber ?? "").Contains(q)));
    var ordered=sort switch { "amount_asc"=>rows.OrderBy(x=>x.Amount).ThenBy(x=>x.Id), "amount_desc"=>rows.OrderByDescending(x=>x.Amount).ThenBy(x=>x.Id), "date_asc"=>rows.OrderBy(x=>x.Date).ThenBy(x=>x.Id), _=>rows.OrderByDescending(x=>x.Date).ThenByDescending(x=>x.Id) };
    return await Paged(ordered, page, pageSize, x => x.View());
});
app.MapGet("/api/operations/{id:long}", [Authorize] async (long id,HttpContext ctx,CashDb db,ICashboxAccess access) => { var x = await db.Operations.Include(x => x.Category).Include(x => x.Counterparty).Include(x => x.Author).SingleOrDefaultAsync(x => x.Id == id);if(x is null)return Results.NotFound();await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted);return Results.Ok(x.View()); });
app.MapPost("/api/operations", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (OperationInput x, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx = await db.Database.BeginTransactionAsync();
    await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted);
    var bad = await Validate(x, db); if (bad is not null) return bad;
    if (x.RequestKey == Guid.Empty) return Results.Problem("Требуется ключ запроса.",statusCode:400);
    var existing = await db.Operations.FirstOrDefaultAsync(o=>o.CreatedBy==ctx.UserId() && o.RequestKey==x.RequestKey); if(existing != null) { if(existing.CashboxId!=x.CashboxId || existing.Amount!=x.Amount || existing.Type!=x.Type || existing.Date!=x.Date || existing.Description!=x.Description?.Trim() || existing.CategoryId!=x.CategoryId || existing.CounterpartyId!=x.CounterpartyId || existing.DocumentTypeId!=x.DocumentTypeId) return Results.Problem("Ключ уже использован для другого запроса.",statusCode:409); return Results.Ok(existing.View()); }
    var o = new CashOperation { RequestKey = x.RequestKey, CashboxId = x.CashboxId, Date = x.Date, Type = x.Type!, Amount = x.Amount, Description = x.Description!.Trim(), CategoryId = x.CategoryId, CounterpartyId = x.CounterpartyId, DocumentTypeId = x.DocumentTypeId, CreatedBy = ctx.UserId() };
    await CaptureDocumentSnapshot(db,o,ctx.RequestAborted);
    db.Operations.Add(o); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "operation_created", "operation", o.Id, new { }); await tx.CommitAsync(); return Results.Created($"/api/operations/{o.Id}", o.View());
});
app.MapPatch("/api/operations/{id:long}", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id, OperationPatch x, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx=await db.Database.BeginTransactionAsync();
    var o = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(); if (o is null) return Results.NotFound();
    await access.Require(ctx.UserId(),o.CashboxId,ctx.RequestAborted);
    if ((o.CreatedBy != ctx.UserId() && !ctx.User.IsInRole(Admin) && !ctx.User.IsInRole(Accountant)) || o.Status != "draft") return Results.Problem("Можно изменить только собственный черновик.", statusCode: 403);
    if (o.Version != x.Version) return Results.Problem("Черновик изменён другим пользователем.", statusCode: 409);
    var input = new OperationInput(x.CashboxId ?? o.CashboxId, x.Date ?? o.Date, x.Type ?? o.Type, x.Amount ?? o.Amount, x.Description ?? o.Description, x.CategoryId ?? o.CategoryId, (x.ClearCounterparty?null:x.CounterpartyId ?? o.CounterpartyId), x.DocumentTypeId ?? o.DocumentTypeId, o.RequestKey);
    await access.Require(ctx.UserId(),input.CashboxId,ctx.RequestAborted);
    var bad = await Validate(input, db, o); if (bad is not null) return bad;
    o.CashboxId = input.CashboxId; o.Date = input.Date; o.Type = input.Type!; o.Amount = input.Amount; o.Description = input.Description!.Trim(); o.CategoryId = input.CategoryId; o.CounterpartyId = input.CounterpartyId; o.DocumentTypeId = input.DocumentTypeId; await CaptureDocumentSnapshot(db,o,ctx.RequestAborted); o.Version++; o.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "operation_updated", "operation", id, new { }); await tx.CommitAsync(); return Results.Ok(o.View());
});
app.MapDelete("/api/operations/{id:long}", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id, int version, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx=await db.Database.BeginTransactionAsync();
    var o = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(); if (o is null) return Results.NotFound();
    await access.Require(ctx.UserId(),o.CashboxId,ctx.RequestAborted);
    if ((o.CreatedBy != ctx.UserId() && !ctx.User.IsInRole(Admin) && !ctx.User.IsInRole(Accountant)) || o.Status != "draft") return Results.Problem("Можно удалить только собственный черновик.", statusCode: 403);
    if (o.Version != version) return Results.Problem("Черновик изменён другим пользователем.", statusCode: 409);
    db.Operations.Remove(o); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "operation_deleted", "operation", id, new { }); await tx.CommitAsync(); return Results.NoContent();
});
app.MapPost("/api/operations/{id:long}/post", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id,PostingRequest request,HttpContext ctx,ILedgerService ledger)=>Results.Ok(await ledger.Post(id,request.ExpectedVersion,ctx.UserId(),ctx.User.IsInRole(Admin)||ctx.User.IsInRole(Accountant),ctx.RequestAborted)));
app.MapPost("/api/operations/{id:long}/reversals", [Authorize(Roles = Admin + "," + Accountant)] async (long id, ReversalInput x, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx = await db.Database.BeginTransactionAsync();
    var original = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(); if (original is null) return Results.NotFound();
    await access.Require(ctx.UserId(),original.CashboxId,ctx.RequestAborted);
    if (original.Status != "posted" || original.IsReversal || original.ReversalId.HasValue || await db.Operations.AnyAsync(o=>o.ReversalOfId==id)) return Results.Problem("Эту операцию нельзя сторнировать.", statusCode: 409);
    var date = x.Date ?? Local.Today; if (string.IsNullOrWhiteSpace(x.Reason) || date < original.Date || date > Local.Today) return Results.Problem("Укажите причину и корректную дату.", statusCode: 400);
    var reversal = new CashOperation { CashboxId = original.CashboxId, Date = date, Type = original.Type == "income" ? "expense" : "income", Amount = original.Amount, Description = "СТОРНО: " + original.Description, CategoryId = original.CategoryId, CounterpartyId = original.CounterpartyId, OrganizationNameSnapshot=original.OrganizationNameSnapshot,CashboxNameSnapshot=original.CashboxNameSnapshot,CashboxCodeSnapshot=original.CashboxCodeSnapshot,CategoryNameSnapshot=original.CategoryNameSnapshot,CounterpartyNameSnapshot=original.CounterpartyNameSnapshot,DocumentTypeId = original.DocumentTypeId, CreatedBy = ctx.UserId(), IsReversal = true, ReversalOfId = original.Id, ReversalReason=x.Reason!.Trim() };
    db.Operations.Add(reversal); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "reversal_created", "operation", original.Id, new { reversal.Id, x.Reason }); await tx.CommitAsync(); return Results.Created($"/api/operations/{reversal.Id}", reversal.View());
});

app.MapGet("/api/summary", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,IBalanceService balances,long cashboxId, DateOnly? from, DateOnly? to) =>
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
app.MapGet("/api/notifications", [Authorize] async (HttpContext ctx, CashDb db, ICashboxAccess access, BackupService backups, IBalanceService balanceService) =>
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
app.MapPost("/api/notifications/{key}/read", [Authorize] async (string key, HttpContext ctx, CashDb db, Audit audit) =>
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
app.MapGet("/api/dashboard", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,IBalanceService balanceService,DateOnly? from,DateOnly? to,long? branchId,long? cashboxId,long? categoryId,long? counterpartyId) =>
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
app.MapGet("/api/operations/{id:long}/document.pdf", [Authorize] async (long id,HttpContext ctx,CashDb db,ICashboxAccess access,IPdfReport pdf)=>{
    var row=await db.Operations.Include(x=>x.Counterparty).Include(x=>x.Category).SingleOrDefaultAsync(x=>x.Id==id);if(row==null)return Results.NotFound();var org=await db.Organizations.SingleAsync();var box=await db.Cashboxes.AsNoTracking().SingleAsync(x=>x.Id==row.CashboxId);
    await access.Require(ctx.UserId(),row.CashboxId,ctx.RequestAborted);
    var bytes=pdf.GenerateVoucher(row.OrganizationNameSnapshot??org.Name,org.Currency,row.Type=="income",row.Status=="posted",row.DocumentNumber??$"Черновик №{row.Id}",row.Date,row.CashboxNameSnapshot??box.Name,row.CashboxCodeSnapshot??box.Code,row.CategoryNameSnapshot??row.Category.Name,row.CounterpartyNameSnapshot??row.Counterparty?.Name,row.Description,row.Amount);
    return Results.File(bytes,"application/pdf",$"Faiza-{row.Id}.pdf");
});
app.MapGet("/api/category-summary",[Authorize] async(HttpContext ctx,CashDb db,ICashboxAccess access,long cashboxId,DateOnly from,DateOnly to)=>{await access.Require(ctx.UserId(),cashboxId,ctx.RequestAborted);return Results.Ok(await db.Operations.Where(x=>x.CashboxId==cashboxId&&x.Status=="posted"&&x.Date>=from&&x.Date<=to).GroupBy(x=>new {x.Category.Name,x.Type}).Select(g=>new {g.Key.Name,g.Key.Type,amount=g.Sum(x=>x.Amount)}).OrderByDescending(x=>x.amount).ToListAsync());});
app.MapGet("/api/analytics",[Authorize] async(HttpContext ctx,CashDb db,ICashboxAccess access,long cashboxId,DateOnly from,DateOnly to)=>{await access.Require(ctx.UserId(),cashboxId,ctx.RequestAborted);return Results.Ok(await db.Operations.Where(x=>x.CashboxId==cashboxId && x.Status=="posted" && x.Date>=from && x.Date<=to).GroupBy(x=>new {x.Date,x.Type}).Select(g=>new {g.Key.Date,g.Key.Type,amount=g.Sum(x=>x.Amount)}).OrderBy(x=>x.Date).ToListAsync());});

}


public static async Task Seed(CashDb db, IConfiguration cfg, ILogger log)
{
    if(!await db.Organizations.AnyAsync()) { db.Organizations.Add(new()); await db.SaveChangesAsync(); }
    if (!await db.Roles.AnyAsync()) { db.Roles.AddRange(new Role { Code = Admin, Name = "Администратор" }, new Role { Code = Accountant, Name = "Бухгалтер" }, new Role { Code = Manager, Name = "Руководитель" }, new Role { Code = Auditor, Name = "Аудитор" }, new Role {Code="cashier",Name="Кассир"}); db.Currencies.Add(new Currency { Code = "TJS", Name = "Таджикский сомони" }); await db.SaveChangesAsync(); }
    if (!await db.Users.AnyAsync())
    {
        var pass = cfg["Seed:AdminPassword"];
        if (Password.Valid(pass)) { var u = new User { Login = cfg["Seed:AdminLogin"] ?? "admin", FullName = "Системный администратор", PasswordHash = Password.Hash(pass!), MustChangePassword = true }; u.UserRoles.Add(new UserRole { Role = await db.Roles.SingleAsync(x => x.Code == Admin) }); db.Users.Add(u); await db.SaveChangesAsync(); }
        else log.LogWarning("Первый администратор не создан. Задайте Seed__AdminPassword.");
    }
}


}
