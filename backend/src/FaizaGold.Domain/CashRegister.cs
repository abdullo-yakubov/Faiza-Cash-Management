namespace FaizaGold.Domain;

/// <summary>Касса филиала. Все изменения остатка проходят через методы этого класса.</summary>
public sealed class CashRegister
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public required string Name { get; set; }
    public string Currency { get; set; } = "TJS";
    public decimal Balance { get; private set; }
    public bool IsActive { get; set; } = true;

    public Shift OpenShift(int userId, DateTimeOffset now, Shift? currentOpen)
    {
        EnsureActive();
        if (currentOpen is not null)
            throw new DomainException("В этой кассе уже открыта смена.");
        return new Shift
        {
            CashRegisterId = Id,
            OpenedById = userId,
            OpenedAt = now,
            OpeningBalance = Balance,
        };
    }

    public void CloseShift(Shift shift, int userId, DateTimeOffset now, decimal? countedAmount)
    {
        EnsureShift(shift);
        if (countedAmount is < 0)
            throw new DomainException("Фактическая сумма не может быть отрицательной.");
        if (countedAmount is { } c && decimal.Round(c, 2) != c)
            throw new DomainException("Фактическая сумма может содержать не больше двух знаков после запятой.");
        shift.ClosedById = userId;
        shift.ClosedAt = now;
        shift.ClosingBalance = Balance;
        shift.CountedAmount = countedAmount;
        shift.Difference = countedAmount is null ? null : countedAmount - Balance;
    }

    /// <summary>Проводит приход или расход и возвращает запись кассовой книги (без номера).</summary>
    public CashOperation Post(Shift shift, OperationKind kind, decimal amount, int userId, DateTimeOffset now,
        Category? category = null, int? counterpartyId = null, string? description = null, int? transferId = null)
    {
        EnsureActive();
        EnsureShift(shift);
        Money.EnsureValidAmount(amount);

        var delta = kind switch
        {
            OperationKind.Income or OperationKind.TransferIn => amount,
            OperationKind.Expense or OperationKind.TransferOut => -amount,
            _ => throw new DomainException("Этот вид операции нельзя провести напрямую."),
        };

        if (kind is OperationKind.Income or OperationKind.Expense)
        {
            if (category is null)
                throw new DomainException("Укажите статью.");
            var expected = kind == OperationKind.Income ? CategoryKind.Income : CategoryKind.Expense;
            if (category.Kind != expected || !category.IsActive)
                throw new DomainException("Статья не подходит для этого вида операции.");
        }

        return Apply(shift, kind, amount, delta, userId, now, category?.Id, counterpartyId, description, transferId);
    }

    /// <summary>Отменяет операцию записью сторно. Отменять можно только в открытой смене той же кассы.</summary>
    public CashOperation Reverse(Shift shift, CashOperation original, int userId, DateTimeOffset now, string reason)
    {
        EnsureActive();
        EnsureShift(shift);
        if (original.CashRegisterId != Id)
            throw new DomainException("Операция относится к другой кассе.");
        if (original.Kind is not (OperationKind.Income or OperationKind.Expense))
            throw new DomainException("Отменить можно только приход или расход. Перевод исправляется обратным переводом.");
        if (original.IsReversed)
            throw new DomainException("Операция уже отменена.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Укажите причину отмены.");

        var reversal = Apply(shift, OperationKind.Reversal, original.Amount, -original.Delta, userId, now,
            original.CategoryId, original.CounterpartyId, $"Сторно {original.Number}: {reason.Trim()}", null);
        reversal.ReversesId = original.Id;
        return reversal;
    }

    private CashOperation Apply(Shift shift, OperationKind kind, decimal amount, decimal delta, int userId,
        DateTimeOffset now, int? categoryId, int? counterpartyId, string? description, int? transferId)
    {
        if (Balance + delta < 0)
            throw new DomainException($"Недостаточно денег в кассе. Остаток {Balance:0.00} {Currency}.");
        Balance += delta;
        return new CashOperation
        {
            Number = "",
            CashRegisterId = Id,
            ShiftId = shift.Id,
            Kind = kind,
            Amount = amount,
            Delta = delta,
            BalanceAfter = Balance,
            CategoryId = categoryId,
            CounterpartyId = counterpartyId,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            TransferId = transferId,
            CreatedById = userId,
            CreatedAt = now,
        };
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new DomainException("Касса закрыта для операций.");
    }

    private void EnsureShift(Shift shift)
    {
        if (shift.CashRegisterId != Id)
            throw new DomainException("Смена относится к другой кассе.");
        if (!shift.IsOpen)
            throw new DomainException("Смена закрыта. Откройте новую смену.");
    }
}

public sealed class Shift
{
    public int Id { get; set; }
    public int CashRegisterId { get; set; }
    public int OpenedById { get; set; }
    public User? OpenedBy { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public decimal OpeningBalance { get; set; }
    public int? ClosedById { get; set; }
    public User? ClosedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal? ClosingBalance { get; set; }

    /// <summary>Фактически пересчитанная сумма при закрытии.</summary>
    public decimal? CountedAmount { get; set; }

    /// <summary>Излишек (плюс) или недостача (минус).</summary>
    public decimal? Difference { get; set; }

    public bool IsOpen => ClosedAt is null;
}
