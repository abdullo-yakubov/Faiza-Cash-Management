using FaizaGold.Api.Data;
using FaizaGold.Api.Infrastructure;
using FaizaGold.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Features;

public static class ShiftEndpoints
{
    public sealed record CloseShiftRequest(decimal? CountedAmount);

    public sealed record ShiftDto(int Id, int CashRegisterId, string OpenedBy, DateTimeOffset OpenedAt, decimal OpeningBalance,
        string? ClosedBy, DateTimeOffset? ClosedAt, decimal? ClosingBalance, decimal? CountedAmount, decimal? Difference,
        decimal Income, decimal Expense, int OperationsCount);

    public static void MapShifts(this RouteGroupBuilder api)
    {
        api.MapGet("/registers/{id:int}/shift", async (int id, AppDbContext db, CurrentUser me, CancellationToken ct) =>
        {
            await EnsureCanViewAsync(me, id, ct);
            var shift = await db.Shifts.Where(s => s.CashRegisterId == id && s.ClosedAt == null).Select(s => s.Id).SingleOrDefaultAsync(ct);
            return shift == 0 ? Results.NoContent() : Results.Ok(await LoadAsync(db, shift, ct));
        });

        api.MapGet("/shifts", async (int registerId, AppDbContext db, CurrentUser me, CancellationToken ct) =>
        {
            await EnsureCanViewAsync(me, registerId, ct);
            var ids = await db.Shifts.Where(s => s.CashRegisterId == registerId).OrderByDescending(s => s.OpenedAt)
                .Take(50).Select(s => s.Id).ToListAsync(ct);
            var result = new List<ShiftDto>();
            foreach (var sid in ids) result.Add(await LoadAsync(db, sid, ct));
            return result;
        });

        api.MapPost("/registers/{id:int}/shift/open", async (int id, AppDbContext db, CurrentUser me, AuditLog audit,
            TimeProvider clock, CancellationToken ct) =>
        {
            await me.EnsureCanOperateAsync(id, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var register = await db.LockRegisterAsync(id, ct) ?? throw new NotFoundException("Касса не найдена.");
            var current = await db.Shifts.SingleOrDefaultAsync(s => s.CashRegisterId == id && s.ClosedAt == null, ct);
            var shift = register.OpenShift(me.Id, clock.GetUtcNow(), current);
            db.Shifts.Add(shift);
            await db.SaveChangesAsync(ct);
            audit.Add("shift_opened", "shift", shift.Id, new { register = register.Name, shift.OpeningBalance });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(await LoadAsync(db, shift.Id, ct));
        }).RequireAuthorization(Policies.Operate);

        api.MapPost("/registers/{id:int}/shift/close", async (int id, CloseShiftRequest req, AppDbContext db, CurrentUser me,
            AuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            await me.EnsureCanOperateAsync(id, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var register = await db.LockRegisterAsync(id, ct) ?? throw new NotFoundException("Касса не найдена.");
            var shift = await db.Shifts.SingleOrDefaultAsync(s => s.CashRegisterId == id && s.ClosedAt == null, ct)
                        ?? throw new DomainException("В кассе нет открытой смены.");
            register.CloseShift(shift, me.Id, clock.GetUtcNow(), req.CountedAmount);
            audit.Add("shift_closed", "shift", shift.Id,
                new { register = register.Name, shift.ClosingBalance, shift.CountedAmount, shift.Difference });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(await LoadAsync(db, shift.Id, ct));
        }).RequireAuthorization(Policies.Operate);
    }

    internal static async Task EnsureCanViewAsync(CurrentUser me, int registerId, CancellationToken ct)
    {
        if (!await me.CanAccessRegisterAsync(registerId, ct))
            throw new ForbiddenException("Нет доступа к этой кассе.");
    }

    private static async Task<ShiftDto> LoadAsync(AppDbContext db, int shiftId, CancellationToken ct)
    {
        var s = await db.Shifts.AsNoTracking().Include(x => x.OpenedBy).Include(x => x.ClosedBy).SingleAsync(x => x.Id == shiftId, ct);
        var totals = await db.Operations.Where(o => o.ShiftId == shiftId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Income = g.Where(o => o.Delta > 0).Sum(o => o.Delta),
                Expense = g.Where(o => o.Delta < 0).Sum(o => -o.Delta),
                Count = g.Count(),
            }).SingleOrDefaultAsync(ct);
        return new ShiftDto(s.Id, s.CashRegisterId, s.OpenedBy!.FullName, s.OpenedAt, s.OpeningBalance, s.ClosedBy?.FullName,
            s.ClosedAt, s.ClosingBalance, s.CountedAmount, s.Difference, totals?.Income ?? 0, totals?.Expense ?? 0, totals?.Count ?? 0);
    }
}
