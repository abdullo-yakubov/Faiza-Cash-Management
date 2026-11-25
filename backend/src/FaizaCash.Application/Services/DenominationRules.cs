using FaizaCash.Domain;

namespace FaizaCash.Application;

public sealed record DenominationItem(decimal Value, string? Kind);
public sealed record DenominationsInput(string? Currency, List<DenominationItem>? Items);
public sealed record CashInventoryInput(long CashboxId, List<DenominationCount>? Denominations, List<string>? Commission, string? Note);

/// <summary>
/// Пересчёт наличных по номиналам (контракт волны 1, п.4). Все суммы — только decimal:
/// 0,10 × 3 = 0,30 ровно, без ошибок двоичной арифметики.
/// </summary>
public static class DenominationRules
{
    public const int MaxCount = 1_000_000;

    public static decimal Total(IEnumerable<DenominationCount> lines) => lines.Sum(x => x.Value * x.Count);

    /// <summary>
    /// Проверка строк пересчёта против справочника активных номиналов валюты кассы. Вид (банкнота/монета) необязателен:
    /// если не указан и номинал существует в обоих видах, строка принимается без вида — на сумму это не влияет.
    /// Возвращает нормализованные строки (с видом из справочника, если он однозначен) или текст ошибки.
    /// </summary>
    public static (List<DenominationCount>? Lines, string? Error) Normalize(IReadOnlyList<DenominationCount>? lines, IReadOnlyCollection<(decimal Value, string Kind)> allowed)
    {
        if (lines is null || lines.Count == 0) return (null, "Укажите пересчёт по номиналам.");
        if (lines.Count > 100) return (null, "Слишком много строк пересчёта.");
        var result = new List<DenominationCount>();
        foreach (var line in lines)
        {
            if (line.Count is < 0 or > MaxCount) return (null, "Количество купюр и монет — целое число от 0 до 1 000 000.");
            if (line.Kind is not (null or "banknote" or "coin")) return (null, "Вид номинала: banknote или coin.");
            var matches = allowed.Where(a => a.Value == line.Value && (line.Kind == null || a.Kind == line.Kind)).ToList();
            if (matches.Count == 0) return (null, $"Номинал {line.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} отсутствует в справочнике валюты кассы.");
            var kind = line.Kind ?? (matches.Count == 1 ? matches[0].Kind : null);
            if (result.Any(r => r.Value == line.Value && (r.Kind == kind || r.Kind == null || kind == null)))
                return (null, "Каждый номинал указывается в пересчёте один раз.");
            result.Add(new DenominationCount(line.Value, kind, line.Count));
        }
        return Total(result) > 9999999999999999.99m ? (null, "Сумма пересчёта слишком велика.") : (result, null);
    }

    /// <summary>Комиссия инвентаризации: от 1 до 10 человек, ФИО 3–160 символов, без повторов.</summary>
    public static (string[]? Members, string? Error) Commission(IReadOnlyList<string>? names)
    {
        var members = (names ?? []).Select(x => x?.Trim() ?? "").ToArray();
        if (members.Length is 0 or > 10) return (null, "Укажите состав комиссии: от 1 до 10 человек.");
        if (members.Any(x => x.Length is < 3 or > 160 || x.Contains('\0'))) return (null, "ФИО члена комиссии — от 3 до 160 символов.");
        if (members.Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Length) return (null, "Члены комиссии не должны повторяться.");
        return (members, null);
    }

    /// <summary>Справочник номиналов для PUT /api/denominations: 1–50 позиций, положительные суммы до дирама, без повторов.</summary>
    public static string? CatalogError(IReadOnlyList<DenominationItem>? items)
    {
        if (items is null || items.Count is 0 or > 50) return "Справочник должен содержать от 1 до 50 номиналов.";
        if (items.Any(x => !Money.IsValid(x.Value) || x.Kind is not ("banknote" or "coin"))) return "Номинал — положительная сумма с точностью до дирама, вид — banknote или coin.";
        if (items.Select(x => (x.Value, x.Kind)).Distinct().Count() != items.Count) return "Номиналы в справочнике не должны повторяться.";
        return null;
    }
}
