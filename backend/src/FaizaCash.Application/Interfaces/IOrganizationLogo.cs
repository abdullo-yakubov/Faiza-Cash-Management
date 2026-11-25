using FaizaCash.Domain;
namespace FaizaCash.Application;

/// <summary>Текущий логотип организации для шапки PDF (кэш в памяти; источник — таблица OrganizationLogos).</summary>
public interface IOrganizationLogo
{
    byte[]? Current { get; }
    void Set(byte[]? image);
}


/// <summary>Данные акта инвентаризации наличных денежных средств для печатной формы.</summary>
public sealed record InventoryActData(string Organization, string Currency, long Number, DateTime CountedAtLocal, string Cashbox, string CashboxCode,
    IReadOnlyList<DenominationCount> Denominations, decimal Expected, decimal Actual, decimal Difference, IReadOnlyList<string> Commission, string? Note);
