using FaizaCash.Application;
using Microsoft.EntityFrameworkCore;

namespace FaizaCash.Infrastructure;

public sealed class BalanceService(CashDb db) : IBalanceService
{
    public async Task<decimal> BalanceAt(long cashboxId, DateOnly date, CancellationToken cancellationToken = default)
    {
        if (!await db.Cashboxes.AnyAsync(x => x.Id == cashboxId, cancellationToken)) throw new KeyNotFoundException("Касса не найдена.");
        return (await BalancesAt([cashboxId], date, cancellationToken)).GetValueOrDefault(cashboxId);
    }

    public async Task<IReadOnlyDictionary<long, decimal>> BalancesAt(IReadOnlyCollection<long> cashboxIds, DateOnly date, CancellationToken cancellationToken = default)
    {
        var ids = (await ExistingOn(cashboxIds, date, cancellationToken)).ToArray();
        var openings = await db.Openings.AsNoTracking().Where(x => ids.Contains(x.CashboxId) && x.Date <= date)
            .ToDictionaryAsync(x => x.CashboxId, x => x.Amount, cancellationToken);
        var deltas = await db.Operations.AsNoTracking().Where(x => ids.Contains(x.CashboxId) && x.Status == "posted" && x.Date <= date)
            .GroupBy(x => x.CashboxId).Select(g => new { g.Key, Delta = g.Sum(x => x.Type == "income" ? x.Amount : -x.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Delta, cancellationToken);
        return ids.ToDictionary(id => id, id => openings.GetValueOrDefault(id) + deltas.GetValueOrDefault(id));
    }

    public async Task<PeriodOpening> PeriodOpening(IReadOnlyCollection<long> cashboxIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var ids = cashboxIds.Distinct().ToArray();
        var activeIds = (await ExistingOn(ids, from, cancellationToken)).ToArray();
        var initial = await db.Openings.AsNoTracking().Where(x => activeIds.Contains(x.CashboxId) && x.Date <= from).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var prior = await db.Operations.AsNoTracking().Where(x => ids.Contains(x.CashboxId) && x.Status == "posted" && x.Date < from)
            .SumAsync(x => (decimal?)(x.Type == "income" ? x.Amount : -x.Amount), cancellationToken) ?? 0m;
        var activations = await db.Openings.AsNoTracking().Where(x => ids.Contains(x.CashboxId) && x.Date > from && x.Date <= to)
            .GroupBy(x => x.Date).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Amount, cancellationToken);
        return new PeriodOpening(initial + prior, activations);
    }

    public async Task<IReadOnlyList<DailyBalance>> DailySeries(IReadOnlyCollection<long> cashboxIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (from > to) throw new BusinessException("Укажите корректный период.", 400);
        var ids = cashboxIds.Distinct().ToArray();
        var start = await PeriodOpening(ids, from, to, cancellationToken);
        var deltas = await db.Operations.AsNoTracking().Where(x => ids.Contains(x.CashboxId) && x.Status == "posted" && x.Date >= from && x.Date <= to)
            .GroupBy(x => x.Date).Select(g => new { g.Key, Delta = g.Sum(x => x.Type == "income" ? x.Amount : -x.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Delta, cancellationToken);
        var running = start.Opening;
        var series = new List<DailyBalance>(to.DayNumber - from.DayNumber + 1);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            running += start.Activations.GetValueOrDefault(day) + deltas.GetValueOrDefault(day);
            series.Add(new DailyBalance(day, running));
            if (day == DateOnly.MaxValue) break;
        }
        return series;
    }

    public async Task<IReadOnlyCollection<long>> ExistingOn(IReadOnlyCollection<long> cashboxIds, DateOnly date, CancellationToken cancellationToken = default)
    {
        var ids = cashboxIds.Distinct().ToArray();
        var notYet = await db.Openings.AsNoTracking().Where(x => ids.Contains(x.CashboxId) && x.Date > date).Select(x => x.CashboxId).ToListAsync(cancellationToken);
        return ids.Except(notYet).ToArray();
    }
}
