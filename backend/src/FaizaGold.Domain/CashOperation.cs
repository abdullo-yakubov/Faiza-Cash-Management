namespace FaizaGold.Domain;

/// <summary>
/// Запись кассовой книги. Записи никогда не изменяются по сумме и не удаляются:
/// отмена оформляется отдельной записью «сторно». Поэтому остаток кассы всегда
/// равен сумме поля <see cref="Delta"/> всех её записей.
/// </summary>
public sealed class CashOperation
{
    public long Id { get; set; }
    public required string Number { get; set; }
    public int CashRegisterId { get; set; }
    public int ShiftId { get; set; }
    public OperationKind Kind { get; set; }

    /// <summary>Сумма документа, всегда положительная.</summary>
    public decimal Amount { get; set; }

    /// <summary>Влияние на остаток: плюс для прихода, минус для расхода.</summary>
    public decimal Delta { get; set; }

    public decimal BalanceAfter { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? CounterpartyId { get; set; }
    public Counterparty? Counterparty { get; set; }
    public string? Description { get; set; }
    public int? TransferId { get; set; }
    public int CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Для сторно: какую операцию отменяет эта запись.</summary>
    public long? ReversesId { get; set; }

    /// <summary>Для отменённой операции: запись сторно, которая её отменила.</summary>
    public long? ReversedById { get; set; }

    public bool IsReversed => ReversedById is not null;

    public static string Prefix(OperationKind kind) => kind switch
    {
        OperationKind.Income => "ПКО",
        OperationKind.Expense => "РКО",
        OperationKind.TransferIn => "ПЕР",
        OperationKind.TransferOut => "ПЕР",
        OperationKind.Reversal => "СТР",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
