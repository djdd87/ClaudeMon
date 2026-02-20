using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ClaudeMon.Views.Controls;

public partial class UsageGaugeControl : UserControl
{
    // Arc geometry constants
    private const double CanvasSize = 160.0;
    private const double StrokeWidth = 10.0;
    private const double Radius = 70.0; // (140 / 2) to match the background ellipse
    private const double CenterX = CanvasSize / 2.0;
    private const double CenterY = CanvasSize / 2.0;

    // Frozen status brushes for arc coloring
    private static readonly SolidColorBrush GreenBrush = CreateFrozenBrush(0x4C, 0xAF, 0x50);
    private static readonly SolidColorBrush AmberBrush = CreateFrozenBrush(0xFF, 0x98, 0x00);
    private static readonly SolidColorBrush RedBrush = CreateFrozenBrush(0xF4, 0x43, 0x36);
    private static readonly SolidColorBrush GreyBrush = CreateFrozenBrush(0x9E, 0x9E, 0x9E);

    public static readonly DependencyProperty PercentageProperty =
        DependencyProperty.Register(nameof(Percentage), typeof(double), typeof(UsageGaugeControl),
            new PropertyMetadata(0.0, OnPercentageChanged));

    public static readonly DependencyProperty TierTextProperty =
        DependencyProperty.Register(nameof(TierText), typeof(string), typeof(UsageGaugeControl),
            new PropertyMetadata(string.Empty));

    public double Percentage
    {
        get => (double)GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, value);
    }

    public string TierText
    {
        get => (string)GetValue(TierTextProperty);
        set => SetValue(TierTextProperty, value);
    }

    public UsageGaugeControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateArc();
    }

    private static void OnPercentageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((UsageGaugeControl)d).UpdateArc();
    }

    private void UpdateArc()
    {
        if (ArcPath is null || PercentageText is null)
            return;

        double percentage = Percentage;
        bool isUnknown = percentage < 0;

        // Update center text
        if (isUnknown)
        {
            PercentageText.Text = "?";
            EstLabel.Text = "Unknown";
        }
        else
        {
            double clamped = Math.Clamp(percentage, 0.0, 100.0);
            PercentageText.Text = clamped >= 99.5 ? "Limit" : $"~{clamped:0}%";
            EstLabel.Text = "Est.";
        }

        // Center the text elements horizontally within the canvas.
        // They are positioned at Canvas.Left=80 (center), so we shift left by half their width.
        PercentageText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        PercentageTextTranslate.X = -PercentageText.DesiredSize.Width / 2.0;

        EstLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        EstLabelTranslate.X = -EstLabel.DesiredSize.Width / 2.0;

        // Determine arc color
        SolidColorBrush arcBrush = GetBrushForPercentage(percentage);
        ArcPath.Stroke = arcBrush;

        // Build arc geometry
        if (isUnknown || percentage <= 0)
        {
            ArcPath.Data = Geometry.Empty;
            return;
        }

        double clampedPercent = Math.Clamp(percentage, 0.0, 100.0);
        ArcPath.Data = BuildArcGeometry(clampedPercent);
    }

    private static Geometry BuildArcGeometry(double percentage)
    {
        if (percentage <= 0.0)
            return Geometry.Empty;

        // Cap at 99.9% to avoid full-circle degeneracy with arcs
        double effectivePercent = Math.Min(percentage, 99.9);
        double sweepAngleDeg = effectivePercent / 100.0 * 360.0;
        double sweepAngleRad = sweepAngleDeg * Math.PI / 180.0;

        // Start at 12 o'clock (-90 degrees in standard math coordinates)
        const double startAngleRad = -Math.PI / 2.0;
        double endAngleRad = startAngleRad + sweepAngleRad;

        var startPoint = new Point(
            CenterX + Radius * Math.Cos(startAngleRad),
            CenterY + Radius * Math.Sin(startAngleRad));

        var endPoint = new Point(
            CenterX + Radius * Math.Cos(endAngleRad),
            CenterY + Radius * Math.Sin(endAngleRad));

        bool isLargeArc = sweepAngleDeg > 180.0;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(startPoint, false, false);
            context.ArcTo(
                endPoint,
                new Size(Radius, Radius),
                0,
                isLargeArc,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush GetBrushForPercentage(double percentage) => percentage switch
    {
        >= 0 and <= 50 => GreenBrush,
        > 50 and <= 80 => AmberBrush,
        > 80 and <= 100 => RedBrush,
        _ => GreyBrush
    };

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
