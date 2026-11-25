using FaizaGold.Api.Features;

namespace FaizaGold.Tests;

public class CsvTests
{
    [Theory]
    [InlineData("обычный текст", "обычный текст")]
    [InlineData("=HYPERLINK(1)", "'=HYPERLINK(1)")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("say \"hi\";", "\"say \"\"hi\"\";\"")]
    public void Csv_escapes_values_and_blocks_formulas(string input, string expected) =>
        Assert.Equal(expected, ReportEndpoints.Csv(input));
}
