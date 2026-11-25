using FaizaGold.Api.Data;
using FaizaGold.Api.Infrastructure;
using FaizaGold.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Features;

public static class OperationEndpoints
{
    public sealed record CreateOperationRequest(int CashRegisterId, OperationKind Kind, decimal Amount, int CategoryId,
        int? CounterpartyId, string? Description);

    public sealed record ReverseRequest(string Reason);

    public sealed record OperationDto(long Id, string Number, int CashRegisterId, OperationKind Kind, decimal Amount, decimal Delta,
        decimal BalanceAfter, string? Category, string? Counterparty, string? Description, string CreatedBy,
        DateTimeOffset CreatedAt, bool IsReversed, long? ReversesId);

    public static void MapOperations(this RouteGroupBuilder api)
    {
        api.MapGet("/operations", async (int registerId, DateOnly? from, DateOnly? to, AppDbContext db, CurrentUser me,
            BusinessCalendar calendar, TimeProvider clock, CancellationToken ct) =>
        {
            await ShiftEndpoints.EnsureCanViewAsync(me, registerId, ct);
            var start = calendar.StartOfDay(from ?? calendar.Today(clock));
            var end = calendar.StartOfDay((to ?? from ?? calendar.Today(clock)).AddDays(1));
            return await Project(db.Operations.Where(o => o.CashRegisterId == registerId && o.CreatedAt >= start && o.CreatedAt < end)
                .OrderByDescending(o => o.Id).Take(1000)).ToListAsync(ct);
        });

        api.MapPost("/operations", async (CreateOperationRequest req, AppDbContext db, CurrentUser me, AuditLog audit,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (req.Kind is not (OperationKind.Income or OperationKind.Expense))
                throw new ValidationException("Можно провести только приход или расход.");
            await me.EnsureCanOperateAsync(req.CashRegisterId, ct);
            var description = Check.Optional(req.Description, "Основание", 500);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var register = await db.LockRegisterAsync(req.CashRegisterId, ct) ?? throw new NotFoundException("Касса не найдена.");
            var shift = await OpenShiftAsync(db, register.Id, ct);
            var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == req.CategoryId, ct);
            if (req.CounterpartyId is { } cpId && !await db.Counterparties.AnyAsync(c => c.Id == cpId && c.IsActive, ct))
                throw new ValidationException("Контрагент не найден.");

            var op = register.Post(shift, req.Kind, req.Amount, me.Id, clock.GetUtcNow(), category, req.CounterpartyId, description);
            op.Number = await db.NextNumberAsync(req.Kind, ct);
            db.Operations.Add(op);
            await db.SaveChangesAsync(ct);
            audit.Add("operation_created", "operation", op.Number, new { register = register.Name, op.Kind, op.Amount, register.Currency });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/operations/{op.Id}", await Project(db.Operations.Where(o => o.Id == op.Id)).SingleAsync(ct));
        }).RequireAuthorization(Policies.Operate);

        api.MapPost("/operations/{id:long}/reverse", async (long id, ReverseRequest req, AppDbContext db, CurrentUser me,
            AuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var registerId = await db.Operations.Where(o => o.Id == id).Select(o => (int?)o.CashRegisterId).SingleOrDefaultAsync(ct)
                             ?? throw new NotFoundException("Операция не найдена.");
            await me.EnsureCanOperateAsync(registerId, ct);
            var reason = Check.Text(req.Reason, "Причина", 300);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var register = await db.LockRegisterAsync(registerId, ct) ?? throw new NotFoundException("Касса не найдена.");
            var original = await db.Operations.SingleAsync(o => o.Id == id, ct);
            var shift = await OpenShiftAsync(db, register.Id, ct);
            var reversal = register.Reverse(shift, original, me.Id, clock.GetUtcNow(), reason);
            reversal.Number = await db.NextNumberAsync(OperationKind.Reversal, ct);
            db.Operations.Add(reversal);
            await db.SaveChangesAsync(ct);
            original.ReversedById = reversal.Id;
            audit.Add("operation_reversed", "operation", original.Number, new { reversal = reversal.Number, reason });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(await Project(db.Operations.Where(o => o.Id == reversal.Id)).SingleAsync(ct));
        }).RequireAuthorization(Policies.Operate);
    }

    internal static async Task<Shift> OpenShiftAsync(AppDbContext db, int registerId, CancellationToken ct) =>
        await db.Shifts.SingleOrDefaultAsync(s => s.CashRegisterId == registerId && s.ClosedAt == null, ct)
        ?? throw new DomainException("В кассе нет открытой смены. Сначала откройте смену.");

    internal static IQueryable<OperationDto> Project(IQueryable<CashOperation> source) =>
        source.AsNoTracking().Select(o => new OperationDto(o.Id, o.Number, o.CashRegisterId, o.Kind, o.Amount, o.Delta,
            o.BalanceAfter, o.Category!.Name, o.Counterparty!.Name, o.Description, o.CreatedBy!.FullName, o.CreatedAt,
            o.ReversedById != null, o.ReversesId));
}
