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

/// <summary>Модуль «Representatives»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class RepresentativesEndpoints
{
    public static IEndpointRouteBuilder MapRepresentativesEndpoints(this IEndpointRouteBuilder app)
    {
        var representatives = app.MapGroup("/api/representatives").RequireAuthorization().WithTags("Representatives");
        var receipts = app.MapGroup("/api/representative-receipts").RequireAuthorization().WithTags("Representatives");
representatives.MapGet("", [Authorize(Roles = Admin + "," + Accountant + "," + Manager + "," + Auditor)] async (CashDb db, CancellationToken ct) =>
    Results.Ok(await db.Users.AsNoTracking().Where(u=>u.Active).OrderBy(u=>u.FullName).Select(u=>new{u.Id,u.FullName}).Take(MaxListSize).ToListAsync(ct)));
receipts.MapGet("", [Authorize] async (CashDb db, Guid? representativeId, string? status, int? page, int? pageSize, HttpContext ctx, ICashboxAccess access) =>
{
    if(status is not (null or "collected" or "handed_over" or "accepted" or "cancelled"))return Results.Problem("Недопустимый статус квитанции.",statusCode:400);
    var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
    var query=db.RepresentativeReceipts.AsNoTracking().Where(x=>allowed.Contains(x.CashboxId)&&(!representativeId.HasValue||x.RepresentativeId==representativeId)&&(status==null||x.Status==status)).OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id)
        .Select(x=>new{x.Id,x.RepresentativeId,representative=db.Users.Where(u=>u.Id==x.RepresentativeId).Select(u=>u.FullName).FirstOrDefault(),x.CounterpartyId,counterparty=x.Counterparty.Name,x.BasisType,x.BasisReference,x.Amount,x.CashboxId,x.Status,x.RequestKey,x.CreatedAt,x.HandedOverAt,x.AcceptedBy,x.AcceptedAt});
    return await ListPage(ctx,query,page,pageSize,ctx.RequestAborted);
});
receipts.MapPost("/{id:long}/cancel", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (long id, CancelInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
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
receipts.MapPost("", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (RepresentativeReceiptInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
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
receipts.MapPost("/{id:long}/handover", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (long id, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) => { var r=await db.RepresentativeReceipts.FindAsync(id); if(r is null)return Results.NotFound(); await access.Require(ctx.UserId(),r.CashboxId,ctx.RequestAborted); if(r.Status!="collected")return Results.Conflict(); r.Status="handed_over";r.HandedOverAt=DateTime.UtcNow;await db.SaveChangesAsync();await audit.Add(ctx.UserId(),"representative_receipt_handed_over","representative_receipt",id,new{});return Results.Ok(r); });
receipts.MapPost("/{id:long}/accept", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) => { var r=await db.RepresentativeReceipts.FindAsync(id); if(r is null)return Results.NotFound(); await access.Require(ctx.UserId(),r.CashboxId,ctx.RequestAborted); if(r.Status!="handed_over")return Results.Conflict(); r.Status="accepted";r.AcceptedAt=DateTime.UtcNow;r.AcceptedBy=ctx.UserId();await db.SaveChangesAsync();await audit.Add(ctx.UserId(),"representative_receipt_accepted","representative_receipt",id,new{});return Results.Ok(r); });
        return app;
    }
}
