namespace FaizaCash.Application;

/// <summary>Balance of a set of cashboxes at the start of a reporting period.</summary>
/// <param name="Opening">Initial balances dated on or before <c>from</c> plus posted movements dated before <c>from</c>.</param>
/// <param name="Activations">Initial balances whose date falls inside (<c>from</c>, <c>to</c>], keyed by activation date.</param>
public sealed record PeriodOpening(decimal Opening, IReadOnlyDictionary<DateOnly, decimal> Activations)
{
    public decimal ActivationTotal => Activations.Values.Sum();
}

public sealed record DailyBalance(DateOnly Date, decimal Balance);

/// <summary>
/// Single source of truth for cash balances (FG7-001). Rules:
/// a cashbox initial balance (CashboxOpening) counts from its date; only posted operations count
/// (reversals and both sides of transfers are ordinary posted operations, so nothing is counted twice);
/// dates are business dates in Asia/Dushanbe.
/// </summary>
public interface IBalanceService
{
    Task<decimal> BalanceAt(long cashboxId, DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, decimal>> BalancesAt(IReadOnlyCollection<long> cashboxIds, DateOnly date, CancellationToken cancellationToken = default);
    Task<PeriodOpening> PeriodOpening(IReadOnlyCollection<long> cashboxIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyBalance>> DailySeries(IReadOnlyCollection<long> cashboxIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    /// <summary>Cashboxes that exist on <paramref name="date"/>: no initial balance, or initial balance dated on/before it.</summary>
    Task<IReadOnlyCollection<long>> ExistingOn(IReadOnlyCollection<long> cashboxIds, DateOnly date, CancellationToken cancellationToken = default);
}
