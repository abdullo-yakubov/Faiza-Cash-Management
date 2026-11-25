namespace FaizaCash.Application;

/// <summary>
/// Согласование крупных расходов руководителем (контракт волны 1, п.1).
/// Статусы: none → pending → approved | rejected; любое изменение черновика возвращает none.
/// </summary>
public static class ApprovalRules
{
    public const string None = "none", Pending = "pending", Approved = "approved", Rejected = "rejected";
    public static readonly string[] Statuses = [None, Pending, Approved, Rejected];

    /// <summary>
    /// Расход требует согласования, если порог задан и сумма не меньше порога.
    /// Сторно не согласуется: это исправление уже проведённого документа, его создаёт только бухгалтер.
    /// </summary>
    public static bool RequiresApproval(string type, decimal amount, decimal? threshold, bool isReversal) =>
        threshold.HasValue && type == "expense" && !isReversal && amount >= threshold.Value;

    /// <summary>Проведение разрешено, если согласование не требуется или уже получено.</summary>
    public static bool CanPost(string type, decimal amount, decimal? threshold, bool isReversal, string approvalStatus) =>
        !RequiresApproval(type, amount, threshold, isReversal) || approvalStatus == Approved;

    /// <summary>Причина, по которой черновик нельзя отправить на согласование, или null.</summary>
    public static string? SubmitError(string status, string type, decimal amount, decimal? threshold, bool isReversal, string approvalStatus)
    {
        if (status != "draft") return "На согласование отправляется только черновик.";
        if (type != "expense") return "На согласование отправляется только расход.";
        if (!RequiresApproval(type, amount, threshold, isReversal)) return "Этот расход не требует согласования руководителя.";
        if (approvalStatus == Pending) return "Расход уже ожидает согласования.";
        if (approvalStatus == Approved) return "Расход уже согласован.";
        return null;
    }

    /// <summary>Причина, по которой решение принять нельзя, или null. Автор документа не может его согласовать.</summary>
    public static string? DecisionError(string status, string approvalStatus, Guid authorId, Guid deciderId)
    {
        if (status != "draft") return "Решение принимается только по черновику.";
        if (approvalStatus != Pending) return "Расход не ожидает согласования.";
        if (authorId == deciderId) return "Нельзя согласовать или отклонить собственный документ.";
        return null;
    }

    /// <summary>Причина отклонения обязательна: 1–500 символов без нулевых байтов.</summary>
    public static string? NormalizeReason(string? reason)
    {
        var value = reason?.Trim();
        return string.IsNullOrEmpty(value) || value.Length > 500 || value.Contains('\0') ? null : value;
    }
}
