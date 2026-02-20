using System.Globalization;
using ClaudeMon.Converters;

namespace ClaudeMon.Tests.Converters;

public class TokenFormatterConverterTests
{
    private readonly TokenFormatterConverter _converter = new();

    private string Convert(object value)
        => (string)_converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(500L, "500")]
    [InlineData(999L, "999")]
    public void BelowThousand_ReturnsPlainNumber(long input, string expected)
    {
        Assert.Equal(expected, Convert(input));
    }

    [Theory]
    [InlineData(1_000L, "1K")]
    [InlineData(1_500L, "1.5K")]
    [InlineData(999_999L, "1000K")]
    public void Thousands_FormatsWithK(long input, string expected)
    {
        Assert.Equal(expected, Convert(input));
    }

    [Theory]
    [InlineData(1_000_000L, "1M")]
    [InlineData(2_500_000L, "2.5M")]
    public void Millions_FormatsWithM(long input, string expected)
    {
        Assert.Equal(expected, Convert(input));
    }

    [Fact]
    public void IntInput_Handled()
    {
        Assert.Equal("1.5K", Convert(1500));
    }

    [Fact]
    public void DoubleInput_Handled()
    {
        Assert.Equal("1.5K", Convert(1500.0));
    }

    [Fact]
    public void NonNumeric_ReturnsZero()
    {
        Assert.Equal("0", Convert("not a number"));
    }
}
