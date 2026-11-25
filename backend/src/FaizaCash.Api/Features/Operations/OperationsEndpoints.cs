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

/// <summary>Модуль «Operations»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var operations = app.MapGroup("/api/operations").RequireAuthorization().WithTags("Operations");
operations.MapGet("", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,long? cashboxId, DateOnly? from, DateOnly? to, string? type, string? status, long? categoryId, long? counterpartyId, string? q, string? sort, int page = 1, int pageSize = 25) =>
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
operations.MapGet("/{id:long}", [Authorize] async (long id,HttpContext ctx,CashDb db,ICashboxAccess access) => { var x = await db.Operations.Include(x => x.Category).Include(x => x.Counterparty).Include(x => x.Author).SingleOrDefaultAsync(x => x.Id == id);if(x is null)return Results.NotFound();await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted);return Results.Ok(x.View()); });
operations.MapPost("", [Authorize(Roles = CashOperators)] async (OperationInput x, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
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
operations.MapPatch("/{id:long}", [Authorize(Roles = CashOperators)] async (long id, OperationPatch x, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx=await db.Database.BeginTransactionAsync();
    var o = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(); if (o is null) return Results.NotFound();
    await access.Require(ctx.UserId(),o.CashboxId,ctx.RequestAborted);
    if ((o.CreatedBy != ctx.UserId() && !ctx.User.IsInRole(Accountant)) || o.Status != "draft") return Results.Problem("Можно изменить только собственный черновик.", statusCode: 403);
    if (o.Version != x.Version) return Results.Problem("Черновик изменён другим пользователем.", statusCode: 409);
    var input = new OperationInput(x.CashboxId ?? o.CashboxId, x.Date ?? o.Date, x.Type ?? o.Type, x.Amount ?? o.Amount, x.Description ?? o.Description, x.CategoryId ?? o.CategoryId, (x.ClearCounterparty?null:x.CounterpartyId ?? o.CounterpartyId), x.DocumentTypeId ?? o.DocumentTypeId, o.RequestKey);
    await access.Require(ctx.UserId(),input.CashboxId,ctx.RequestAborted);
    var bad = await Validate(input, db, o); if (bad is not null) return bad;
    o.CashboxId = input.CashboxId; o.Date = input.Date; o.Type = input.Type!; o.Amount = input.Amount; o.Description = input.Description!.Trim(); o.CategoryId = input.CategoryId; o.CounterpartyId = input.CounterpartyId; o.DocumentTypeId = input.DocumentTypeId; await CaptureDocumentSnapshot(db,o,ctx.RequestAborted); o.Version++; o.UpdatedAt = DateTime.UtcNow;
    var approvalBefore = o.ApprovalStatus; o.ApprovalStatus = ApprovalRules.None; o.ApprovedBy = null; o.ApprovedAt = null; o.ApprovalComment = null;
    await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "operation_updated", "operation", id, new { }); if (approvalBefore != ApprovalRules.None) await audit.Add(ctx.UserId(), "approval_reset", "operation", id, new { before = approvalBefore }); await tx.CommitAsync(); return Results.Ok(o.View());
});
operations.MapDelete("/{id:long}", [Authorize(Roles = CashOperators)] async (long id, int version, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx=await db.Database.BeginTransactionAsync();
    var o = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(); if (o is null) return Results.NotFound();
    await access.Require(ctx.UserId(),o.CashboxId,ctx.RequestAborted);
    if ((o.CreatedBy != ctx.UserId() && !ctx.User.IsInRole(Accountant)) || o.Status != "draft") return Results.Problem("Можно удалить только собственный черновик.", statusCode: 403);
    if (o.Version != version) return Results.Problem("Черновик изменён другим пользователем.", statusCode: 409);
    db.Operations.Remove(o); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "operation_deleted", "operation", id, new { }); await tx.CommitAsync(); return Results.NoContent();
});
operations.MapPost("/{id:long}/post", [Authorize(Roles = CashOperators)] async (long id,PostingRequest request,HttpContext ctx,ILedgerService ledger)=>Results.Ok(await ledger.Post(id,request.ExpectedVersion,ctx.UserId(),ctx.User.IsInRole(Accountant),ctx.RequestAborted)));
operations.MapPost("/{id:long}/reversals", [Authorize(Roles = Accountant)] async (long id, ReversalInput x, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx = await db.Database.BeginTransactionAsync();
    var original = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(); if (original is null) return Results.NotFound();
    await access.Require(ctx.UserId(),original.CashboxId,ctx.RequestAborted);
    if (original.Status != "posted" || original.IsReversal || original.ReversalId.HasValue || await db.Operations.AnyAsync(o=>o.ReversalOfId==id)) return Results.Problem("Эту операцию нельзя сторнировать.", statusCode: 409);
    var date = x.Date ?? Local.Today; if (string.IsNullOrWhiteSpace(x.Reason) || date < original.Date || date > Local.Today) return Results.Problem("Укажите причину и корректную дату.", statusCode: 400);
    var reversal = new CashOperation { CashboxId = original.CashboxId, Date = date, Type = original.Type == "income" ? "expense" : "income", Amount = original.Amount, Description = "СТОРНО: " + original.Description, CategoryId = original.CategoryId, CounterpartyId = original.CounterpartyId, OrganizationNameSnapshot=original.OrganizationNameSnapshot,CashboxNameSnapshot=original.CashboxNameSnapshot,CashboxCodeSnapshot=original.CashboxCodeSnapshot,CategoryNameSnapshot=original.CategoryNameSnapshot,CounterpartyNameSnapshot=original.CounterpartyNameSnapshot,DocumentTypeId = original.DocumentTypeId, CreatedBy = ctx.UserId(), IsReversal = true, ReversalOfId = original.Id, ReversalReason=x.Reason!.Trim() };
    db.Operations.Add(reversal); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "reversal_created", "operation", original.Id, new { reversal.Id, x.Reason }); await tx.CommitAsync(); return Results.Created($"/api/operations/{reversal.Id}", reversal.View());
});
operations.MapGet("/{id:long}/document.pdf", [Authorize] async (long id,HttpContext ctx,CashDb db,ICashboxAccess access,IPdfReport pdf)=>{
    var row=await db.Operations.Include(x=>x.Counterparty).Include(x=>x.Category).SingleOrDefaultAsync(x=>x.Id==id);if(row==null)return Results.NotFound();var org=await db.Organizations.SingleAsync();var box=await db.Cashboxes.AsNoTracking().SingleAsync(x=>x.Id==row.CashboxId);
    await access.Require(ctx.UserId(),row.CashboxId,ctx.RequestAborted);
    var bytes=pdf.GenerateVoucher(row.OrganizationNameSnapshot??org.Name,org.Currency,row.Type=="income",row.Status=="posted",row.DocumentNumber??$"Черновик №{row.Id}",row.Date,row.CashboxNameSnapshot??box.Name,row.CashboxCodeSnapshot??box.Code,row.CategoryNameSnapshot??row.Category.Name,row.CounterpartyNameSnapshot??row.Counterparty?.Name,row.Description,row.Amount);
    return Results.File(bytes,"application/pdf",$"Faiza-{row.Id}.pdf");
});
        return app;
    }
}
