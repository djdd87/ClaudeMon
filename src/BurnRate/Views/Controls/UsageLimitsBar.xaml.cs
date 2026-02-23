using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BurnRate.Models;

namespace BurnRate.Views.Controls;

/// <summary>
/// Displays horizontal usage-limit bars for session, weekly, sonnet, and extra usage.
/// Data is sourced from the live Anthropic API via UsageSummary properties.
/// </summary>
public partial class UsageLimitsBar : UserControl
{
    private static readonly SolidColorBrush GreenBrush = Frozen(0x4C, 0xAF, 0x50);
    private static readonly SolidColorBrush AmberBrush = Frozen(0xFF, 0x98, 0x00);
    private static readonly SolidColorBrush RedBrush = Frozen(0xF4, 0x43, 0x36);
    private static readonly SolidColorBrush GreyBrush = Frozen(0x9E, 0x9E, 0x9E);

    public static readonly DependencyProperty UsageProperty =
        DependencyProperty.Register(
            nameof(Usage), typeof(UsageSummary), typeof(UsageLimitsBar),
            new PropertyMetadata(null, OnUsageChanged));

    /// <summary>
    /// The UsageSummary instance to read bar data from.
    /// </summary>
    public UsageSummary Usage
    {
        get => (UsageSummary)GetValue(UsageProperty);
        set => SetValue(UsageProperty, value);
    }

    /// <summary>
    /// Observable collection of computed bar items for the ItemsControl.
    /// </summary>
    public ObservableCollection<UsageLimitBarItem> BarItems { get; } = [];

    public UsageLimitsBar()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            if (Usage is UsageSummary u)
                u.PropertyChanged -= OnUsagePropertyChanged;
        };
    }

    private static void OnUsageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageLimitsBar control)
        {
            if (e.OldValue is UsageSummary oldUsage)
                oldUsage.PropertyChanged -= control.OnUsagePropertyChanged;
            if (e.NewValue is UsageSummary newUsage)
                newUsage.PropertyChanged += control.OnUsagePropertyChanged;
            control.UpdateBars();
        }
    }

    private void OnUsagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UsageSummary.SessionPercentage)
            or nameof(UsageSummary.WeeklyPercentage)
            or nameof(UsageSummary.SonnetPercentage)
            or nameof(UsageSummary.ExtraUsagePercentage)
            or nameof(UsageSummary.ExtraUsageEnabled)
            or nameof(UsageSummary.IsLive))
        {
            Dispatcher.InvokeAsync(UpdateBars);
        }
    }

    private void UpdateBars()
    {
        BarItems.Clear();
        var u = Usage;
        if (u == null || !u.IsLive) return;

        // 1. Session (5-hour window)
        AddBar("Session", u.SessionPercentage,
            FormatResetTooltip("Session resets", u.SessionResetsAt));

        // 2. Week (7-day combined)
        AddBar("Week", u.WeeklyPercentage,
            FormatResetTooltip("Week resets", u.WeeklyResetsAt));

        // 3. Sonnet (7-day Sonnet-specific, only if API returned data)
        if (u.SonnetPercentage >= 0)
            AddBar("Sonnet", u.SonnetPercentage,
                FormatResetTooltip("Sonnet resets", u.SonnetResetsAt));

        // 4. Extra Usage (only if enabled on this account)
        if (u.ExtraUsageEnabled && u.ExtraUsagePercentage >= 0)
        {
            var symbol = u.ExtraUsageCurrency?.ToUpperInvariant() == "USD" ? "$" : u.ExtraUsageCurrency + " ";
            var tooltip = $"{symbol}{u.ExtraUsageUsed:F2} / {symbol}{u.ExtraUsageLimit:F2} spent";
            AddBar("Extra", u.ExtraUsagePercentage, tooltip);
        }
    }

    private void AddBar(string label, double percentage, string tooltip)
    {
        var clamped = Math.Clamp(percentage, 0, 100);
        BarItems.Add(new UsageLimitBarItem
        {
            Label = label,
            Percentage = clamped,
            BarBrush = GetColorBrush(percentage),
            Tooltip = tooltip
        });
    }

    private static SolidColorBrush GetColorBrush(double percentage) => percentage switch
    {
        < 0 => GreyBrush,
        <= 50 => GreenBrush,
        <= 80 => AmberBrush,
        _ => RedBrush
    };

    private static string FormatResetTooltip(string prefix, DateTime? resetsAt)
    {
        if (!resetsAt.HasValue) return prefix;
        var remaining = resetsAt.Value - DateTime.Now;
        if (remaining.TotalMinutes < 1) return $"{prefix} shortly";
        if (remaining.TotalHours < 1) return $"{prefix} in {(int)remaining.TotalMinutes}m";
        if (remaining.TotalHours < 24) return $"{prefix} in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"{prefix} {resetsAt.Value:MMM dd h:mm tt}";
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Represents a single row in the usage limits bar section.
/// </summary>
public class UsageLimitBarItem
{
    public string Label { get; set; } = string.Empty;
    public double Percentage { get; set; }
    public SolidColorBrush BarBrush { get; set; } = Brushes.Gray;
    public string Tooltip { get; set; } = string.Empty;
}
