using System.Security.Cryptography;
using FaizaCash.Application;
using FaizaCash.Domain;
using FaizaCash.Infrastructure;
using FaizaCash.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using static FaizaCash.Api.RoleNames;

namespace FaizaCash.Api;

/// <summary>
/// Вложения первичных документов (контракт волны 1, п.2). Доступ — по кассе владельца;
/// тип файла — по сигнатуре; удаление после проведения/принятия запрещено в коде и триггером БД.
/// </summary>
public static class AttachmentsEndpoints
{
    internal sealed record Owner(long CashboxId, string Status, Guid? AuthorId);

    public static IEndpointRouteBuilder MapAttachmentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/attachments").RequireAuthorization().WithTags("Attachments");
        group.MapGet("", [Authorize] async (string? ownerType, long? ownerId, HttpContext ctx, CashDb db, ICashboxAccess access) =>
        {
            if (ownerType is null || !AttachmentRules.OwnerTypes.Contains(ownerType) || ownerId is not > 0)
                return Results.Problem("Укажите ownerType (operation, receipt, advance) и ownerId.", statusCode: 400);
            var owner = await FindOwner(db, ownerType, ownerId.Value, lockRow: false, ctx.RequestAborted);
            if (owner is null) return Results.NotFound();
            await access.Require(ctx.UserId(), owner.CashboxId, ctx.RequestAborted);
            return Results.Ok(await List(db, ownerType, ownerId.Value, ctx.RequestAborted));
        });
        group.MapPost("", [Authorize(Roles = Accountant + "," + Cashier + "," + Manager)] async (HttpContext ctx, CashDb db, ICashboxAccess access, AttachmentStorage storage, Audit audit) =>
        {
            if (!ctx.Request.HasFormContentType) return Results.Problem("Ожидается multipart/form-data с полями ownerType, ownerId и file.", statusCode: 400);
            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
            var ownerType = form["ownerType"].ToString();
            if (!AttachmentRules.OwnerTypes.Contains(ownerType) || !long.TryParse(form["ownerId"], out var ownerId) || ownerId <= 0)
                return Results.Problem("Укажите ownerType (operation, receipt, advance) и ownerId.", statusCode: 400);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.Problem("Приложите непустой файл.", statusCode: 400);
            if (file.Length > AttachmentRules.MaxBytes) return Results.Problem("Размер файла — не больше 5 МБ.", statusCode: 400);
            byte[] content;
            await using (var stream = file.OpenReadStream()) { using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, ctx.RequestAborted); content = buffer.ToArray(); }
            var contentType = FileSignatures.Detect(content);
            if (contentType is null || !FileSignatures.Documents.Contains(contentType))
                return Results.Problem("Допустимы только JPEG, PNG, WebP и PDF. Тип определяется по содержимому файла, а не по расширению.", statusCode: 400);

            await using var tx = await db.Database.BeginTransactionAsync(ctx.RequestAborted);
            var owner = await FindOwner(db, ownerType, ownerId, lockRow: true, ctx.RequestAborted);
            if (owner is null) return Results.NotFound();
            await access.Require(ctx.UserId(), owner.CashboxId, ctx.RequestAborted);
            if (Denied(ctx, ownerType, owner) is { } denied) return denied;
            if (AttachmentRules.IsLocked(ownerType, owner.Status)) return Results.Problem("Документ уже проведён или принят: вложения не меняются.", statusCode: 409);
            if (await db.Attachments.CountAsync(x => x.OwnerType == ownerType && x.OwnerId == ownerId, ctx.RequestAborted) >= AttachmentRules.MaxPerOwner)
                return Results.Problem("К документу можно приложить не больше 10 файлов.", statusCode: 409);
            var attachment = new Attachment { OwnerType = ownerType, OwnerId = ownerId, CashboxId = owner.CashboxId, FileName = FileSignatures.SafeFileName(file.FileName, contentType), ContentType = contentType, SizeBytes = content.LongLength, Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), UploadedBy = ctx.UserId() };
            await storage.SaveAsync(attachment.Id, content, ctx.RequestAborted);
            try
            {
                db.Attachments.Add(attachment);
                await db.SaveChangesAsync(ctx.RequestAborted);
                await audit.Add(ctx.UserId(), "attachment_added", ownerType, ownerId, new { attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.Sha256 });
                await tx.CommitAsync(ctx.RequestAborted);
            }
            catch { storage.Delete(attachment.Id); throw; }
            return Results.Created($"/api/attachments/{attachment.Id}", View(attachment));
        });
        group.MapGet("/{id:guid}/content", [Authorize] async (Guid id, HttpContext ctx, CashDb db, ICashboxAccess access, AttachmentStorage storage) =>
        {
            var attachment = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ctx.RequestAborted);
            if (attachment is null) return Results.NotFound();
            await access.Require(ctx.UserId(), attachment.CashboxId, ctx.RequestAborted);
            var stream = storage.Open(id);
            if (stream is null) return Results.Problem("Файл вложения не найден в хранилище.", statusCode: 404);
            return Results.File(stream, attachment.ContentType, attachment.FileName);
        });
        group.MapDelete("/{id:guid}", [Authorize(Roles = Accountant + "," + Cashier + "," + Manager)] async (Guid id, HttpContext ctx, CashDb db, ICashboxAccess access, AttachmentStorage storage, Audit audit) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ctx.RequestAborted);
            var attachment = await db.Attachments.SingleOrDefaultAsync(x => x.Id == id, ctx.RequestAborted);
            if (attachment is null) return Results.NotFound();
            var owner = await FindOwner(db, attachment.OwnerType, attachment.OwnerId, lockRow: true, ctx.RequestAborted);
            await access.Require(ctx.UserId(), attachment.CashboxId, ctx.RequestAborted);
            if (owner is null) return Results.NotFound();
            if (Denied(ctx, attachment.OwnerType, owner) is { } denied) return denied;
            if (AttachmentRules.IsLocked(attachment.OwnerType, owner.Status)) return Results.Problem("Вложение нельзя удалить: документ уже проведён или принят.", statusCode: 409);
            db.Attachments.Remove(attachment);
            await db.SaveChangesAsync(ctx.RequestAborted);
            await audit.Add(ctx.UserId(), "attachment_deleted", attachment.OwnerType, attachment.OwnerId, new { attachment.Id, attachment.FileName, attachment.Sha256 });
            await tx.CommitAsync(ctx.RequestAborted);
            storage.Delete(id);
            return Results.NoContent();
        });
        return app;
    }

    internal static object View(Attachment x) => new { x.Id, x.OwnerType, x.OwnerId, x.FileName, x.ContentType, x.SizeBytes, x.Sha256, x.UploadedBy, x.UploadedAt };

    internal static async Task<List<object>> List(CashDb db, string ownerType, long ownerId, CancellationToken ct) =>
        (await db.Attachments.AsNoTracking().Where(x => x.OwnerType == ownerType && x.OwnerId == ownerId).OrderBy(x => x.UploadedAt).ThenBy(x => x.Id).ToListAsync(ct)).Select(View).ToList();

    private static IResult? Denied(HttpContext ctx, string ownerType, Owner owner) =>
        AttachmentRules.CanModify(ownerType, ctx.User.IsInRole(Accountant), ctx.User.IsInRole(Cashier), ctx.User.IsInRole(Manager), owner.AuthorId == ctx.UserId())
            ? null
            : Results.Problem("Нет права изменять вложения этого документа.", statusCode: 403);

    /// <summary>Касса, статус и автор владельца; при lockRow строка владельца блокируется до конца транзакции (гонка с проведением).</summary>
    internal static async Task<Owner?> FindOwner(CashDb db, string ownerType, long ownerId, bool lockRow, CancellationToken ct)
    {
        switch (ownerType)
        {
            case "operation":
                var op = lockRow
                    ? await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={ownerId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct)
                    : await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ownerId, ct);
                return op is null ? null : new Owner(op.CashboxId, op.Status, op.CreatedBy);
            case "receipt":
                var receipt = lockRow
                    ? await db.RepresentativeReceipts.FromSqlInterpolated($"SELECT * FROM \"RepresentativeReceipts\" WHERE \"Id\"={ownerId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct)
                    : await db.RepresentativeReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ownerId, ct);
                return receipt is null ? null : new Owner(receipt.CashboxId, receipt.Status, receipt.CreatedBy);
            case "advance":
                var advance = lockRow
                    ? await db.Advances.FromSqlInterpolated($"SELECT * FROM \"Advances\" WHERE \"Id\"={ownerId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct)
                    : await db.Advances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ownerId, ct);
                return advance is null ? null : new Owner(advance.CashboxId, advance.Status, advance.CreatedBy);
            default:
                return null;
        }
    }
}
