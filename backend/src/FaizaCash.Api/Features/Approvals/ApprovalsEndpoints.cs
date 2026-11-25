using FaizaCash.Application;
using FaizaCash.Domain;
using FaizaCash.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using static FaizaCash.Api.RoleNames;
using static FaizaCash.Api.EndpointHelpers;

namespace FaizaCash.Api;

/// <summary>
/// Согласование расходов руководителем (контракт волны 1, п.1). Правила — в <see cref="ApprovalRules"/>,
/// проверка при проведении — в LedgerService.Post внутри той же транзакции.
/// </summary>
public static class ApprovalsEndpoints
{
    public static IEndpointRouteBuilder MapApprovalsEndpoints(this IEndpointRouteBuilder app)
    {
        var operations = app.MapGroup("/api/operations").RequireAuthorization().WithTags("Approvals");
        operations.MapPost("/{id:long}/submit-approval", [Authorize(Roles = CashOperators)] async (long id, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ctx.RequestAborted);
            var o = await Lock(db, id, ctx.RequestAborted);
            if (o is null) return Results.NotFound();
            await access.Require(ctx.UserId(), o.CashboxId, ctx.RequestAborted);
            if (o.CreatedBy != ctx.UserId() && !ctx.User.IsInRole(Accountant))
                return Results.Problem("Отправить на согласование можно только собственный черновик.", statusCode: 403);
            var threshold = await Threshold(db, ctx.RequestAborted);
            if (ApprovalRules.SubmitError(o.Status, o.Type, o.Amount, threshold, o.IsReversal, o.ApprovalStatus) is { } error)
                return Results.Problem(error, statusCode: 409);
            var before = o.ApprovalStatus;
            Apply(o, ApprovalRules.Pending, null, null);
            await db.SaveChangesAsync(ctx.RequestAborted);
            await audit.Add(ctx.UserId(), "approval_submitted", "operation", o.Id, new { before, o.Amount, threshold });
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.Ok(await View(db, o.Id, ctx.RequestAborted));
        });
        operations.MapPost("/{id:long}/approve", [Authorize(Roles = Manager)] async (long id, ApprovalInput? x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
        {
            var comment = x?.Comment?.Trim();
            if (comment is { Length: > 500 } || comment?.Contains('\0') == true) return Results.Problem("Комментарий — не длиннее 500 символов.", statusCode: 400);
            return await Decide(id, ApprovalRules.Approved, string.IsNullOrEmpty(comment) ? null : comment, "approval_approved", ctx, db, access, audit);
        });
        operations.MapPost("/{id:long}/reject", [Authorize(Roles = Manager)] async (long id, CancelInput? x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
        {
            var reason = ApprovalRules.NormalizeReason(x?.Reason);
            if (reason is null) return Results.Problem("Укажите причину отклонения (до 500 символов).", statusCode: 400);
            return await Decide(id, ApprovalRules.Rejected, reason, "approval_rejected", ctx, db, access, audit);
        });
        app.MapGet("/api/approvals", [Authorize] async (HttpContext ctx, CashDb db, ICashboxAccess access, string? status, long? cashboxId, int page = 1, int pageSize = 25) =>
        {
            var state = status ?? ApprovalRules.Pending;
            if (state is not (ApprovalRules.Pending or ApprovalRules.Approved or ApprovalRules.Rejected))
                return Results.Problem("Статус согласования: pending, approved или rejected.", statusCode: 400);
            if (cashboxId.HasValue) await access.Require(ctx.UserId(), cashboxId.Value, ctx.RequestAborted);
            var allowed = await access.AllowedCashboxes(ctx.UserId(), ctx.RequestAborted);
            var query = db.Operations.AsNoTracking().Include(x => x.Category).Include(x => x.Counterparty).Include(x => x.Author)
                .Where(x => allowed.Contains(x.CashboxId) && x.ApprovalStatus == state && (!cashboxId.HasValue || x.CashboxId == cashboxId))
                .OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id);
            return await Paged(query, page, pageSize, x => x.View());
        }).WithTags("Approvals");
        return app;
    }

    private static Task<CashOperation?> Lock(CashDb db, long id, CancellationToken ct) =>
        db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct);

    private static Task<decimal?> Threshold(CashDb db, CancellationToken ct) =>
        db.Organizations.AsNoTracking().Select(x => x.ExpenseApprovalThreshold).SingleAsync(ct);

    /// <summary>Смена статуса согласования считается изменением черновика: версия растёт, клиент проводит актуальную версию.</summary>
    private static void Apply(CashOperation o, string status, Guid? decider, string? comment)
    {
        o.ApprovalStatus = status; o.ApprovedBy = decider; o.ApprovedAt = decider.HasValue ? DateTime.UtcNow : null; o.ApprovalComment = comment;
        o.Version++; o.UpdatedAt = DateTime.UtcNow;
    }

    private static async Task<IResult> Decide(long id, string status, string? comment, string action, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ctx.RequestAborted);
        var o = await Lock(db, id, ctx.RequestAborted);
        if (o is null) return Results.NotFound();
        await access.Require(ctx.UserId(), o.CashboxId, ctx.RequestAborted);
        if (ApprovalRules.DecisionError(o.Status, o.ApprovalStatus, o.CreatedBy, ctx.UserId()) is { } error)
            return Results.Problem(error, statusCode: 409);
        Apply(o, status, ctx.UserId(), comment);
        await db.SaveChangesAsync(ctx.RequestAborted);
        await audit.Add(ctx.UserId(), action, "operation", o.Id, new { o.Amount, comment });
        await tx.CommitAsync(ctx.RequestAborted);
        return Results.Ok(await View(db, o.Id, ctx.RequestAborted));
    }

    private static async Task<object> View(CashDb db, long id, CancellationToken ct) =>
        (await db.Operations.AsNoTracking().Include(x => x.Category).Include(x => x.Counterparty).Include(x => x.Author).SingleAsync(x => x.Id == id, ct)).View();
}
