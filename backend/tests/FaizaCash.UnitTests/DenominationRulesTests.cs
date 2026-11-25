using FaizaCash.Application;
using FaizaCash.Domain;
using Xunit;

namespace FaizaCash.UnitTests;

public class DenominationRulesTests
{
    private static readonly (decimal, string)[] Tjs = [(500m, "banknote"), (100m, "banknote"), (5m, "banknote"), (5m, "coin"), (0.10m, "coin"), (0.05m, "coin")];

    [Fact]
    public void TotalIsExactDecimal()
    {
        var (lines, error) = DenominationRules.Normalize([new(0.10m, null, 3), new(500m, null, 1)], Tjs);
        Assert.Null(error);
        Assert.Equal(500.30m, DenominationRules.Total(lines!));
        Assert.Equal("coin", lines![0].Kind);       // однозначный номинал получает вид из справочника
    }

    [Fact]
    public void AmbiguousValueWithoutKindIsAcceptedOnce()
    {
        var (lines, error) = DenominationRules.Normalize([new(5m, null, 2)], Tjs);
        Assert.Null(error); Assert.Null(lines![0].Kind);
        Assert.NotNull(DenominationRules.Normalize([new(5m, null, 1), new(5m, "coin", 1)], Tjs).Error);
        Assert.Null(DenominationRules.Normalize([new(5m, "banknote", 1), new(5m, "coin", 1)], Tjs).Error);
    }

    [Fact]
    public void UnknownNegativeOrHugeCountsAreRejected()
    {
        Assert.NotNull(DenominationRules.Normalize([new(7m, null, 1)], Tjs).Error);
        Assert.NotNull(DenominationRules.Normalize([new(100m, null, -1)], Tjs).Error);
        Assert.NotNull(DenominationRules.Normalize([new(100m, null, 1_000_001)], Tjs).Error);
        Assert.NotNull(DenominationRules.Normalize([new(100m, "paper", 1)], Tjs).Error);
        Assert.NotNull(DenominationRules.Normalize([], Tjs).Error);
    }

    [Fact]
    public void CommissionNeedsDistinctMembers()
    {
        Assert.Null(DenominationRules.Commission(["Каримов А.", "Саидова М."]).Error);
        Assert.NotNull(DenominationRules.Commission([]).Error);
        Assert.NotNull(DenominationRules.Commission(["Каримов А.", "каримов а."]).Error);
        Assert.NotNull(DenominationRules.Commission(["Я"]).Error);
    }

    [Fact]
    public void CatalogRejectsDuplicatesAndInvalidValues()
    {
        Assert.Null(DenominationRules.CatalogError([new(1m, "coin"), new(1m, "banknote")]));
        Assert.NotNull(DenominationRules.CatalogError([new(1m, "coin"), new(1m, "coin")]));
        Assert.NotNull(DenominationRules.CatalogError([new(0.001m, "coin")]));
        Assert.NotNull(DenominationRules.CatalogError([]));
    }
}
