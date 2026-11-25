namespace FaizaCash.Api;

/// <summary>Коды ролей и наборы ролей для [Authorize(Roles=...)]. Строки совпадают с таблицей Roles.</summary>
/// <remarks>
/// Разделение обязанностей (волна 1): admin настраивает систему и только просматривает финансы,
/// поэтому ни один финансовый набор ниже не содержит admin.
/// </remarks>
public static class RoleNames
{
    public const string Admin = "admin", Accountant = "accountant", Cashier = "cashier", Manager = "manager", Auditor = "auditor";
    /// <summary>Черновики, проведение, смены, переводы, приём квитанций.</summary>
    public const string CashOperators = Accountant + "," + Cashier;
    /// <summary>Задолженности, распределение платежей, квитанции представителей.</summary>
    public const string DebtOperators = Accountant + "," + Manager;
    /// <summary>Инвентаризация кассы.</summary>
    public const string InventoryOperators = Accountant + "," + Manager;
    /// <summary>Изменение курсов валют.</summary>
    public const string RateEditors = Admin + "," + Accountant;
}
