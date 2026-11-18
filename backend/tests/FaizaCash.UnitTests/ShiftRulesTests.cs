using FaizaCash.Application;
using Xunit;

namespace FaizaCash.UnitTests;

public class ShiftRulesTests
{
    [Fact]
    public void ExpenseMustStayWithinCashboxLimit()
    {
        Assert.True(ShiftRules.CanPost(100m, 100m, 40m, "expense"));
        Assert.False(ShiftRules.CanPost(100m, 100m, 100.01m, "expense"));
    }

    [Fact]
    public void IncomeDoesNotBreakCashboxLimit()
    {
        Assert.True(ShiftRules.CanPost(90m, 100m, 10m, "income"));
        Assert.False(ShiftRules.CanPost(90m, 100m, 10.01m, "income"));
    }

    [Fact]
    public void ClosingRequiresOpenShiftAndRecordsDifference()
    {
        Assert.True(ShiftRules.CanClose("open", 150m, 150m));
        Assert.False(ShiftRules.CanClose("closed", 150m, 150m));
        Assert.Equal(-5m, ShiftRules.ClosingDifference(150m, 145m));
    }

    [Theory]
    [InlineData("transfer")]
    [InlineData("refund")]
    [InlineData("")]
    public void UnknownOperationDirectionsAreRejected(string type) =>
        Assert.False(ShiftRules.CanPost(100m, null, 1m, type));
}
