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

/// <summary>Модуль «Transfers»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class TransfersEndpoints
{
    public static IEndpointRouteBuilder MapTransfersEndpoints(this IEndpointRouteBuilder app)
    {
        var transfers = app.MapGroup("/api/transfers").RequireAuthorization().WithTags("Transfers");
transfers.MapPost("", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (CashTransferInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
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
transfers.MapGet("", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,long? cashboxId,DateOnly? from,DateOnly? to,int page=1,int pageSize=25) =>
{
    if(from.HasValue&&to.HasValue&&from>to)return Results.BadRequest();
    var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
    if(!ctx.User.IsInRole(Admin)&&!cashboxId.HasValue)throw new BusinessException("Выберите доступную кассу.",400);
    if(cashboxId.HasValue)await access.Require(ctx.UserId(),cashboxId.Value,ctx.RequestAborted);
    var query=db.Transfers.AsNoTracking().Where(t=>allowed.Contains(t.SourceCashboxId)&&allowed.Contains(t.TargetCashboxId)&&(!cashboxId.HasValue||t.SourceCashboxId==cashboxId||t.TargetCashboxId==cashboxId)&&(!from.HasValue||t.Date>=from)&&(!to.HasValue||t.Date<=to));
    return await Paged(query.OrderByDescending(t=>t.Date).ThenByDescending(t=>t.Id),page,pageSize,TransferView);
});
transfers.MapGet("/{id:long}", [Authorize] async (long id,HttpContext ctx,CashDb db,ICashboxAccess access) =>
{
    var transfer=await db.Transfers.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==id,ctx.RequestAborted);
    if(transfer is null)return Results.NotFound();
    await access.Require(ctx.UserId(),transfer.SourceCashboxId,ctx.RequestAborted);
    await access.Require(ctx.UserId(),transfer.TargetCashboxId,ctx.RequestAborted);
    return Results.Ok(TransferView(transfer));
});
        return app;
    }
}
