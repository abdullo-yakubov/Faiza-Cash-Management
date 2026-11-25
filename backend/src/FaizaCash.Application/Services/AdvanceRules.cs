using FaizaCash.Domain;

namespace FaizaCash.Application;

/// <summary>Строка авансового отчёта для проверки правил (без зависимости от EF).</summary>
public sealed record AdvanceLineInput(DateOnly Date, string? Description, decimal Amount, long CategoryId);
public sealed record AdvanceReportInput(List<AdvanceLineInput>? Lines);
public sealed record AdvanceInput(long OperationId, string? Purpose, DateOnly DueDate);
public sealed record AdvanceSettleInput(long? OperationId);

/// <summary>
/// Авансы подотчётным лицам (контракт волны 1, п.3): issued → reported → closed.
/// Остаток = сумма аванса − израсходовано; отрицательный остаток — перерасход, его возмещает касса.
/// </summary>
public static class AdvanceRules
{
    public const string Issued = "issued", Reported = "reported", Closed = "closed";
    public const int MaxLines = 100;

    /// <summary>Аванс выдаётся только проведённым РКО (не сторно, не перевод, не сторнированным) на контрагента-сотрудника.</summary>
    public static string? IssueError(string opStatus, string opType, bool isReversal, bool reversed, bool transfer, string? counterpartyKind)
    {
        if (opStatus != "posted") return "Аванс оформляется только по проведённому расходному ордеру.";
        if (opType != "expense") return "Аванс выдаётся расходным кассовым ордером (РКО).";
        if (isReversal || reversed || transfer) return "Сторно, сторнированный документ или межкассовый перевод не могут быть авансом.";
        if (counterpartyKind != "employee") return "Получатель аванса должен быть контрагентом вида «сотрудник» (подотчётное лицо).";
        return null;
    }

    public static string? PurposeError(string? purpose, DateOnly dueDate, DateOnly issueDate)
    {
        var value = purpose?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 500 || value.Contains('\0')) return "Укажите назначение аванса (до 500 символов).";
        if (dueDate < issueDate) return "Срок отчёта не может быть раньше даты выдачи.";
        if (dueDate > issueDate.AddYears(1)) return "Срок отчёта — не позже года с даты выдачи.";
        return null;
    }

    /// <summary>Строки отчёта: 1–100, суммы с точностью до дирама, даты от выдачи до сегодня, описание обязательно.</summary>
    public static string? LinesError(IReadOnlyList<AdvanceLineInput>? lines, DateOnly issueDate, DateOnly today)
    {
        if (lines is null || lines.Count is 0 or > MaxLines) return "Авансовый отчёт должен содержать от 1 до 100 строк.";
        foreach (var line in lines)
        {
            if (!Money.IsValid(line.Amount)) return "Сумма строки отчёта — положительная, с точностью до дирама.";
            if (line.Date < issueDate || line.Date > today) return "Дата расхода — не раньше выдачи аванса и не позже сегодняшней.";
            var description = line.Description?.Trim();
            if (string.IsNullOrEmpty(description) || description.Length > 500 || description.Contains('\0')) return "Укажите содержание каждой строки отчёта (до 500 символов).";
            if (line.CategoryId <= 0) return "Укажите категорию расхода в каждой строке.";
        }
        return Spent(lines) > 9999999999999999.99m ? "Сумма отчёта слишком велика." : null;
    }

    public static decimal Spent(IEnumerable<AdvanceLineInput> lines) => lines.Sum(x => x.Amount);
    public static decimal Remainder(decimal amount, decimal spent) => amount - spent;

    /// <summary>
    /// Закрытие: остаток &gt; 0 — проведённый ПКО от сотрудника на сумму остатка; остаток &lt; 0 — проведённый РКО на сумму перерасхода;
    /// остаток = 0 — без операции.
    /// </summary>
    public static string? SettleError(string status, decimal remainder, long counterpartyId, (string Status, string Type, decimal Amount, long? CounterpartyId, bool IsReversal, bool Transfer)? operation)
    {
        if (status != Reported) return status == Closed ? "Аванс уже закрыт." : "Сначала примите авансовый отчёт.";
        if (remainder == 0) return operation is null ? null : "Остаток равен нулю: закрытие без кассовой операции.";
        if (operation is not { } op) return remainder > 0 ? "Укажите проведённый ПКО о возврате остатка аванса." : "Укажите проведённый РКО о возмещении перерасхода.";
        if (op.Status != "posted" || op.IsReversal || op.Transfer) return "Операция закрытия должна быть проведённым документом (не сторно и не переводом).";
        if (op.CounterpartyId != counterpartyId) return "Операция закрытия должна быть оформлена на того же сотрудника.";
        if (remainder > 0 && (op.Type != "income" || op.Amount != remainder)) return $"Нужен ПКО на сумму остатка {remainder.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)}.";
        if (remainder < 0 && (op.Type != "expense" || op.Amount != -remainder)) return $"Нужен РКО на сумму перерасхода {(-remainder).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)}.";
        return null;
    }

    public static bool IsOverdue(string status, DateOnly dueDate, DateOnly today) => status == Issued && dueDate < today;
}
