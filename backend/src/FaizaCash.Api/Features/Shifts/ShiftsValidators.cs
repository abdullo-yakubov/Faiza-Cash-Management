using FaizaCash.Application;
using FaizaCash.Domain;
using FaizaCash.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FaizaCash.Api;

/// <summary>Проверки входных данных кассовых смен и пересчёта по номиналам.</summary>
internal static class ShiftsValidators
{
    public static IResult? Close(ShiftCloseInput x) =>
        x.ClosingBalance < 0 || decimal.Round(x.ClosingBalance, 2) != x.ClosingBalance
            ? Results.Problem("Фактический остаток должен быть неотрицательным, с точностью до дирама.", statusCode: 400)
            : null;

    /// <summary>Активные номиналы валюты кассы.</summary>
    public static async Task<List<(decimal Value, string Kind)>> Allowed(CashDb db, long cashboxId, CancellationToken ct)
    {
        var currency = await db.Cashboxes.Where(c => c.Id == cashboxId).Select(c => c.Currency.Code).SingleAsync(ct);
        return (await db.Denominations.AsNoTracking().Where(d => d.Currency == currency && d.Active).Select(d => new { d.Value, d.Kind }).ToListAsync(ct)).Select(d => (d.Value, d.Kind)).ToList();
    }

    /// <summary>Нормализует пересчёт и сверяет его сумму с фактическим остатком (400 при расхождении).</summary>
    public static async Task<(List<DenominationCount>? Lines, IResult? Error)> Denominations(CashDb db, long cashboxId, IReadOnlyList<DenominationCount> lines, decimal actual, CancellationToken ct)
    {
        var (normalized, error) = DenominationRules.Normalize(lines, await Allowed(db, cashboxId, ct));
        if (error is not null) return (null, Results.Problem(error, statusCode: 400));
        var total = DenominationRules.Total(normalized!);
        if (total != actual)
            return (null, Results.Problem($"Сумма пересчёта по купюрам ({total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}) не совпадает с фактическим остатком ({actual.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}).", statusCode: 400));
        return (normalized, null);
    }
}
