using FaizaCash.Infrastructure;
using Xunit;

namespace FaizaCash.UnitTests;

public class RussianMoneyWordsTests
{
    [Theory]
    [InlineData(123.45, "TJS", "сто двадцать три сомони 45 дирамов")]
    [InlineData(21001.01, "TJS", "двадцать одна тысяча один сомони 01 дирам")]
    [InlineData(1.02, "RUB", "один рубль 02 копейки")]
    [InlineData(0, "RUB", "ноль рублей 00 копеек")]
    [InlineData(-5.5, "TJS", "минус пять сомони 50 дирамов")]
    public void FormatsRussianCurrencyAmount(decimal amount, string currency, string expected) =>
        Assert.Equal(expected, RussianMoneyWords.Format(amount, currency));

    [Fact]
    public void RejectsAmountsOutsideSupportedVoucherRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RussianMoneyWords.Format(1_000_000_000_000_000m, "TJS"));
}
