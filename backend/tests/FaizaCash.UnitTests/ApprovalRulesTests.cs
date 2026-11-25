using FaizaCash.Application;
using Xunit;

namespace FaizaCash.UnitTests;

public class ApprovalRulesTests
{
    [Theory]
    [InlineData("expense", "5000.00", "5000.00", false, true)]   // ровно порог — нужно согласование
    [InlineData("expense", "4999.99", "5000.00", false, false)]
    [InlineData("income", "9000.00", "5000.00", false, false)]   // приход не согласуется
    [InlineData("expense", "9000.00", "5000.00", true, false)]   // сторно не согласуется
    public void RequiresApprovalUsesInclusiveThreshold(string type, string amount, string threshold, bool reversal, bool expected) =>
        Assert.Equal(expected, ApprovalRules.RequiresApproval(type, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(threshold, System.Globalization.CultureInfo.InvariantCulture), reversal));

    [Fact]
    public void ThresholdNullDisablesApproval()
    {
        Assert.False(ApprovalRules.RequiresApproval("expense", 1_000_000m, null, false));
        Assert.True(ApprovalRules.CanPost("expense", 1_000_000m, null, false, ApprovalRules.None));
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("pending", false)]
    [InlineData("rejected", false)]
    [InlineData("approved", true)]
    public void PostingAboveThresholdNeedsApprovedStatus(string status, bool expected) =>
        Assert.Equal(expected, ApprovalRules.CanPost("expense", 6000m, 5000m, false, status));

    [Fact]
    public void SubmitRequiresDraftExpenseAboveThreshold()
    {
        Assert.Null(ApprovalRules.SubmitError("draft", "expense", 6000m, 5000m, false, ApprovalRules.None));
        Assert.Null(ApprovalRules.SubmitError("draft", "expense", 6000m, 5000m, false, ApprovalRules.Rejected));
        Assert.NotNull(ApprovalRules.SubmitError("posted", "expense", 6000m, 5000m, false, ApprovalRules.None));
        Assert.NotNull(ApprovalRules.SubmitError("draft", "income", 6000m, 5000m, false, ApprovalRules.None));
        Assert.NotNull(ApprovalRules.SubmitError("draft", "expense", 100m, 5000m, false, ApprovalRules.None));
        Assert.NotNull(ApprovalRules.SubmitError("draft", "expense", 6000m, 5000m, false, ApprovalRules.Pending));
        Assert.NotNull(ApprovalRules.SubmitError("draft", "expense", 6000m, null, false, ApprovalRules.None));
    }

    [Fact]
    public void AuthorCannotDecideOwnDocument()
    {
        var author = Guid.NewGuid(); var manager = Guid.NewGuid();
        Assert.Null(ApprovalRules.DecisionError("draft", ApprovalRules.Pending, author, manager));
        Assert.NotNull(ApprovalRules.DecisionError("draft", ApprovalRules.Pending, author, author));
        Assert.NotNull(ApprovalRules.DecisionError("draft", ApprovalRules.None, author, manager));
        Assert.NotNull(ApprovalRules.DecisionError("posted", ApprovalRules.Pending, author, manager));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Нет первичного документа ", "Нет первичного документа")]
    public void RejectReasonIsMandatory(string? input, string? expected) => Assert.Equal(expected, ApprovalRules.NormalizeReason(input));
}
