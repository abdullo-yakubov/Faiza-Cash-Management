using FaizaCash.Application;
using Xunit;

namespace FaizaCash.UnitTests;

public class AdvanceRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);

    [Fact]
    public void AdvanceIsIssuedOnlyFromPostedExpenseToEmployee()
    {
        Assert.Null(AdvanceRules.IssueError("posted", "expense", false, false, false, "employee"));
        Assert.NotNull(AdvanceRules.IssueError("draft", "expense", false, false, false, "employee"));
        Assert.NotNull(AdvanceRules.IssueError("posted", "income", false, false, false, "employee"));
        Assert.NotNull(AdvanceRules.IssueError("posted", "expense", true, false, false, "employee"));
        Assert.NotNull(AdvanceRules.IssueError("posted", "expense", false, true, false, "employee"));
        Assert.NotNull(AdvanceRules.IssueError("posted", "expense", false, false, true, "employee"));
        Assert.NotNull(AdvanceRules.IssueError("posted", "expense", false, false, false, "client"));
        Assert.NotNull(AdvanceRules.IssueError("posted", "expense", false, false, false, null));
    }

    [Fact]
    public void ReportLinesAreValidatedAndSummedInDecimal()
    {
        var lines = new List<AdvanceLineInput> { new(Today, "Билет", 0.10m, 1), new(Today, "Такси", 0.20m, 1) };
        Assert.Null(AdvanceRules.LinesError(lines, Today, Today));
        Assert.Equal(0.30m, AdvanceRules.Spent(lines)); // без ошибок double: 0.1 + 0.2 = 0.3
        Assert.NotNull(AdvanceRules.LinesError([], Today, Today));
        Assert.NotNull(AdvanceRules.LinesError([new(Today.AddDays(-1), "До выдачи", 1m, 1)], Today, Today));
        Assert.NotNull(AdvanceRules.LinesError([new(Today, "Дирам", 0.001m, 1)], Today, Today));
        Assert.NotNull(AdvanceRules.LinesError([new(Today, " ", 1m, 1)], Today, Today));
    }

    [Theory]
    [InlineData("1000.00", "700.00", "300.00")]
    [InlineData("500.00", "650.00", "-150.00")]
    [InlineData("200.00", "200.00", "0.00")]
    public void RemainderMayBeNegativeForOverspending(string amount, string spent, string remainder) =>
        Assert.Equal(decimal.Parse(remainder, System.Globalization.CultureInfo.InvariantCulture), AdvanceRules.Remainder(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(spent, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void SettlementNeedsMatchingOperation()
    {
        const long employee = 7;
        Assert.Null(AdvanceRules.SettleError("reported", 0m, employee, null));
        Assert.NotNull(AdvanceRules.SettleError("issued", 0m, employee, null));
        Assert.NotNull(AdvanceRules.SettleError("closed", 0m, employee, null));
        Assert.NotNull(AdvanceRules.SettleError("reported", 300m, employee, null));
        Assert.Null(AdvanceRules.SettleError("reported", 300m, employee, ("posted", "income", 300m, employee, false, false)));
        Assert.NotNull(AdvanceRules.SettleError("reported", 300m, employee, ("posted", "income", 299m, employee, false, false)));
        Assert.NotNull(AdvanceRules.SettleError("reported", 300m, employee, ("posted", "income", 300m, 8, false, false)));
        Assert.NotNull(AdvanceRules.SettleError("reported", 300m, employee, ("draft", "income", 300m, employee, false, false)));
        Assert.Null(AdvanceRules.SettleError("reported", -150m, employee, ("posted", "expense", 150m, employee, false, false)));
        Assert.NotNull(AdvanceRules.SettleError("reported", -150m, employee, ("posted", "income", 150m, employee, false, false)));
        Assert.NotNull(AdvanceRules.SettleError("reported", 0m, employee, ("posted", "income", 1m, employee, false, false)));
    }

    [Fact]
    public void OverdueOnlyWhileIssuedAndPastDue()
    {
        Assert.True(AdvanceRules.IsOverdue("issued", Today.AddDays(-1), Today));
        Assert.False(AdvanceRules.IsOverdue("issued", Today, Today));
        Assert.False(AdvanceRules.IsOverdue("reported", Today.AddDays(-10), Today));
    }
}
