using FaizaGold.Domain;

namespace FaizaGold.Tests;

public class DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly Category Sales = new() { Id = 1, Name = "Выручка", Kind = CategoryKind.Income };
    private static readonly Category Supplier = new() { Id = 2, Name = "Поставщик", Kind = CategoryKind.Expense };

    private static (CashRegister register, Shift shift) OpenRegister()
    {
        var register = new CashRegister { Id = 7, Name = "Касса", Currency = "TJS" };
        var shift = register.OpenShift(1, Now, null);
        return (register, shift);
    }

    [Fact]
    public void Income_then_expense_gives_expected_balance()
    {
        var (register, shift) = OpenRegister();
        var income = register.Post(shift, OperationKind.Income, 1000m, 1, Now, Sales);
        var expense = register.Post(shift, OperationKind.Expense, 250m, 1, Now, Supplier);

        Assert.Equal(750m, register.Balance);
        Assert.Equal(1000m, income.BalanceAfter);
        Assert.Equal(-250m, expense.Delta);
        Assert.Equal(750m, expense.BalanceAfter);
    }

    [Fact]
    public void Expense_larger_than_balance_is_rejected_and_balance_unchanged()
    {
        var (register, shift) = OpenRegister();
        register.Post(shift, OperationKind.Income, 100m, 1, Now, Sales);

        var ex = Assert.Throws<DomainException>(() => register.Post(shift, OperationKind.Expense, 100.01m, 1, Now, Supplier));
        Assert.Contains("Недостаточно денег", ex.Message);
        Assert.Equal(100m, register.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.555)]
    public void Invalid_amounts_are_rejected(decimal amount)
    {
        var (register, shift) = OpenRegister();
        Assert.Throws<DomainException>(() => register.Post(shift, OperationKind.Income, amount, 1, Now, Sales));
    }

    [Fact]
    public void Category_must_match_operation_kind()
    {
        var (register, shift) = OpenRegister();
        Assert.Throws<DomainException>(() => register.Post(shift, OperationKind.Income, 10m, 1, Now, Supplier));
        Assert.Throws<DomainException>(() => register.Post(shift, OperationKind.Income, 10m, 1, Now, null));
    }

    [Fact]
    public void Cannot_post_into_closed_shift_or_open_second_shift()
    {
        var (register, shift) = OpenRegister();
        Assert.Throws<DomainException>(() => register.OpenShift(1, Now, shift));

        register.CloseShift(shift, 1, Now, null);
        Assert.Throws<DomainException>(() => register.Post(shift, OperationKind.Income, 10m, 1, Now, Sales));
    }

    [Fact]
    public void Closing_shift_records_shortage()
    {
        var (register, shift) = OpenRegister();
        register.Post(shift, OperationKind.Income, 500m, 1, Now, Sales);

        register.CloseShift(shift, 2, Now.AddHours(8), countedAmount: 490m);

        Assert.False(shift.IsOpen);
        Assert.Equal(500m, shift.ClosingBalance);
        Assert.Equal(-10m, shift.Difference);
    }

    [Fact]
    public void Reversal_restores_balance_and_cannot_be_repeated()
    {
        var (register, shift) = OpenRegister();
        register.Post(shift, OperationKind.Income, 1000m, 1, Now, Sales);
        var expense = register.Post(shift, OperationKind.Expense, 250m, 1, Now, Supplier);
        expense.Id = 2;
        expense.Number = "РКО-000001";

        var reversal = register.Reverse(shift, expense, 1, Now, "ошибка суммы");
        Assert.Equal(1000m, register.Balance);
        Assert.Equal(250m, reversal.Delta);
        Assert.Equal(2, reversal.ReversesId);

        expense.ReversedById = 3;
        Assert.Throws<DomainException>(() => register.Reverse(shift, expense, 1, Now, "ещё раз"));
    }

    [Fact]
    public void Reversing_income_that_was_already_spent_is_rejected()
    {
        var (register, shift) = OpenRegister();
        var income = register.Post(shift, OperationKind.Income, 100m, 1, Now, Sales);
        register.Post(shift, OperationKind.Expense, 80m, 1, Now, Supplier);

        Assert.Throws<DomainException>(() => register.Reverse(shift, income, 1, Now, "ошибка"));
        Assert.Equal(20m, register.Balance);
    }

    [Fact]
    public void Inactive_register_rejects_operations()
    {
        var (register, shift) = OpenRegister();
        register.IsActive = false;
        Assert.Throws<DomainException>(() => register.Post(shift, OperationKind.Income, 10m, 1, Now, Sales));
    }
}
