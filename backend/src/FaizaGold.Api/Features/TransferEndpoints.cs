using FaizaGold.Api.Data;
using FaizaGold.Api.Infrastructure;
using FaizaGold.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Features;

/// <summary>Перевод денег между кассами одной валюты. Обе записи создаются в одной транзакции.</summary>
public static class TransferEndpoints
{
    public sealed record TransferRequest(int FromRegisterId, int ToRegisterId, decimal Amount, string? Description);

    public sealed record TransferDto(int Id, string From, string To, decimal Amount, string Currency, string? Description,
        string CreatedBy, DateTimeOffset CreatedAt);

    public static void MapTransfers(this RouteGroupBuilder api)
    {
        api.MapGet("/transfers", async (AppDbContext db, CurrentUser me, CancellationToken ct) =>
        {
            var allowed = await me.AllowedRegisterIdsAsync(ct);
            var q = db.Transfers.AsNoTracking();
            if (allowed is not null) q = q.Where(t => allowed.Contains(t.FromRegisterId) || allowed.Contains(t.ToRegisterId));
            return await q.OrderByDescending(t => t.Id).Take(200)
                .Select(t => new TransferDto(t.Id,
                    db.CashRegisters.Where(r => r.Id == t.FromRegisterId).Select(r => r.Branch!.Name + " / " + r.Name).Single(),
                    db.CashRegisters.Where(r => r.Id == t.ToRegisterId).Select(r => r.Branch!.Name + " / " + r.Name).Single(),
                    t.Amount,
                    db.CashRegisters.Where(r => r.Id == t.FromRegisterId).Select(r => r.Currency).Single(),
                    t.Description,
                    db.Users.Where(u => u.Id == t.CreatedById).Select(u => u.FullName).Single(),
                    t.CreatedAt))
                .ToListAsync(ct);
        });

        api.MapPost("/transfers", async (TransferRequest req, AppDbContext db, CurrentUser me, AuditLog audit,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (req.FromRegisterId == req.ToRegisterId)
                throw new ValidationException("Выберите разные кассы.");
            // Отправлять может тот, у кого есть доступ к кассе-источнику; получатель — любая касса той же валюты.
            await me.EnsureCanOperateAsync(req.FromRegisterId, ct);
            var description = Check.Optional(req.Description, "Основание", 500);
            Money.EnsureValidAmount(req.Amount);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Блокируем кассы всегда в одном порядке (по Id), чтобы два встречных перевода не заблокировали друг друга.
            var first = Math.Min(req.FromRegisterId, req.ToRegisterId);
            var second = Math.Max(req.FromRegisterId, req.ToRegisterId);
            var r1 = await db.LockRegisterAsync(first, ct) ?? throw new NotFoundException("Касса не найдена.");
            var r2 = await db.LockRegisterAsync(second, ct) ?? throw new NotFoundException("Касса не найдена.");
            var from = r1.Id == req.FromRegisterId ? r1 : r2;
            var to = r1.Id == req.ToRegisterId ? r1 : r2;
            if (from.Currency != to.Currency)
                throw new DomainException("Переводить можно только между кассами одной валюты.");

            var fromShift = await OperationEndpoints.OpenShiftAsync(db, from.Id, ct);
            var toShift = await db.Shifts.SingleOrDefaultAsync(s => s.CashRegisterId == to.Id && s.ClosedAt == null, ct)
                          ?? throw new DomainException("В кассе-получателе нет открытой смены.");

            var now = clock.GetUtcNow();
            var transfer = new Transfer
            {
                FromRegisterId = from.Id, ToRegisterId = to.Id, Amount = req.Amount, Description = description,
                CreatedById = me.Id, CreatedAt = now,
            };
            db.Transfers.Add(transfer);
            await db.SaveChangesAsync(ct);

            var outOp = from.Post(fromShift, OperationKind.TransferOut, req.Amount, me.Id, now,
                description: $"Перевод в кассу «{to.Name}»" + (description is null ? "" : $": {description}"), transferId: transfer.Id);
            var inOp = to.Post(toShift, OperationKind.TransferIn, req.Amount, me.Id, now,
                description: $"Перевод из кассы «{from.Name}»" + (description is null ? "" : $": {description}"), transferId: transfer.Id);
            outOp.Number = await db.NextNumberAsync(OperationKind.TransferOut, ct);
            inOp.Number = await db.NextNumberAsync(OperationKind.TransferIn, ct);
            db.Operations.AddRange(outOp, inOp);
            audit.Add("transfer_created", "transfer", transfer.Id, new { from = from.Name, to = to.Name, req.Amount, from.Currency });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/transfers/{transfer.Id}", new { transfer.Id, outNumber = outOp.Number, inNumber = inOp.Number });
        }).RequireAuthorization(Policies.Operate);
    }
}
