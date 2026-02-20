using System.Globalization;
using System.Windows.Media;
using ClaudeMon.Converters;

namespace ClaudeMon.Tests.Converters;

public class PercentToColorConverterTests
{
    private readonly PercentToColorConverter _converter = new();

    private SolidColorBrush Convert(object value)
        => (SolidColorBrush)_converter.Convert(value, typeof(Brush), null!, CultureInfo.InvariantCulture);

    private static readonly Color Green = Color.FromRgb(0x4C, 0xAF, 0x50);
    private static readonly Color Amber = Color.FromRgb(0xFF, 0x98, 0x00);
    private static readonly Color Red = Color.FromRgb(0xF4, 0x43, 0x36);
    private static readonly Color Grey = Color.FromRgb(0x9E, 0x9E, 0x9E);

    [Fact]
    public void NegativeValue_ReturnsGrey()
    {
        Assert.Equal(Grey, Convert(-1.0).Color);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(25.0)]
    [InlineData(50.0)]
    public void ZeroToFifty_ReturnsGreen(double pct)
    {
        Assert.Equal(Green, Convert(pct).Color);
    }

    [Theory]
    [InlineData(51.0)]
    [InlineData(65.0)]
    [InlineData(80.0)]
    public void FiftyOneToEighty_ReturnsAmber(double pct)
    {
        Assert.Equal(Amber, Convert(pct).Color);
    }

    [Theory]
    [InlineData(81.0)]
    [InlineData(90.0)]
    [InlineData(100.0)]
    public void EightyOneToHundred_ReturnsRed(double pct)
    {
        Assert.Equal(Red, Convert(pct).Color);
    }

    [Fact]
    public void NonDouble_ReturnsGrey()
    {
        Assert.Equal(Grey, Convert("not a number").Color);
    }
}
