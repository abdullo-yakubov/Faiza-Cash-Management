namespace FaizaGold.Domain;

/// <summary>Роли пользователей системы.</summary>
public enum UserRole
{
    Admin = 1,      // администратор: пользователи, справочники, всё остальное
    Accountant = 2, // бухгалтер: операции по всем кассам, справочники, отчёты
    Cashier = 3,    // кассир: смены и операции только по назначенным кассам
    Manager = 4,    // руководитель: только просмотр, отчёты и журнал
}

/// <summary>Вид кассовой операции.</summary>
public enum OperationKind
{
    Income = 1,      // приходный кассовый ордер (ПКО)
    Expense = 2,     // расходный кассовый ордер (РКО)
    TransferIn = 3,  // поступление по переводу из другой кассы
    TransferOut = 4, // выдача по переводу в другую кассу
    Reversal = 5,    // сторно (отмена ранее проведённой операции)
}

/// <summary>Направление статьи движения денежных средств.</summary>
public enum CategoryKind
{
    Income = 1,
    Expense = 2,
}

public enum CounterpartyKind
{
    Customer = 1,
    Supplier = 2,
    Employee = 3,
    Other = 4,
}

public static class Currencies
{
    public static readonly IReadOnlyList<string> All = ["TJS", "USD", "RUB", "EUR"];

    public static bool IsSupported(string code) => All.Contains(code);
}
