using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ClaudeMon.Converters;

namespace ClaudeMon.Tests.Converters;

public class PercentToArcConverterTests
{
    private readonly PercentToArcConverter _converter = new();

    private object Convert(params object[] values)
        => _converter.Convert(values, typeof(Geometry), null!, CultureInfo.InvariantCulture);

    [Fact]
    public void ZeroPercent_ReturnsEmpty()
    {
        var result = Convert(0.0, 50.0);
        Assert.Equal(Geometry.Empty, result);
    }

    [Fact]
    public void FiftyPercent_ReturnsNonEmptyGeometry()
    {
        var result = Convert(50.0, 50.0);
        Assert.IsType<StreamGeometry>(result);
        Assert.NotEqual(Geometry.Empty, result);
    }

    [Fact]
    public void HundredPercent_ReturnsLargeArc()
    {
        var result = Convert(100.0, 50.0);
        Assert.IsType<StreamGeometry>(result);
        Assert.NotEqual(Geometry.Empty, result);
    }

    [Fact]
    public void MissingValues_ReturnsEmpty()
    {
        // Too few values
        var result = Convert(50.0);
        Assert.Equal(Geometry.Empty, result);
    }

    [Fact]
    public void WrongTypes_ReturnsEmpty()
    {
        var result = Convert("not a number", 50.0);
        Assert.Equal(Geometry.Empty, result);
    }
}
