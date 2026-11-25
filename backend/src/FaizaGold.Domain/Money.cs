namespace FaizaGold.Domain;

public static class Money
{
    public const decimal MaxAmount = 1_000_000_000m;

    /// <summary>Проверяет сумму операции: больше нуля, не больше 2 знаков после запятой.</summary>
    public static decimal EnsureValidAmount(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Сумма должна быть больше нуля.");
        if (amount > MaxAmount)
            throw new DomainException("Сумма слишком большая.");
        if (decimal.Round(amount, 2) != amount)
            throw new DomainException("Сумма может содержать не больше двух знаков после запятой.");
        return amount;
    }
}
