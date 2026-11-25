namespace FaizaCash.Application;

/// <summary>Текущий логотип организации для шапки PDF (кэш в памяти; источник — таблица OrganizationLogos).</summary>
public interface IOrganizationLogo
{
    byte[]? Current { get; }
    void Set(byte[]? image);
}

/// <summary>Строка пересчёта наличных по номиналам. Сумма считается только в decimal.</summary>
public sealed record DenominationCount(decimal Value, string? Kind, int Count)
{
    public decimal Total => Value * Count;
}

/// <summary>Данные акта инвентаризации наличных денежных средств для печатной формы.</summary>
public sealed record InventoryActData(string Organization, string Currency, long Number, DateTime CountedAtLocal, string Cashbox, string CashboxCode,
    IReadOnlyList<DenominationCount> Denominations, decimal Expected, decimal Actual, decimal Difference, IReadOnlyList<string> Commission, string? Note);
