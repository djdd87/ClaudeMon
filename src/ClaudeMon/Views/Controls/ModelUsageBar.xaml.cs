using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClaudeMon.Views.Controls;

/// <summary>
/// A horizontal stacked bar chart displaying token usage by model.
/// Each model gets a color-coded bar proportional to its token count.
/// </summary>
public partial class ModelUsageBar : UserControl
{
    /// <summary>
    /// Rotating palette for model bar colors.
    /// </summary>
    private static readonly string[] Palette =
    [
        "#D97706", // amber / claude brand
        "#3B82F6", // blue
        "#10B981", // emerald
        "#8B5CF6", // violet
        "#EC4899"  // pink
    ];

    /// <summary>
    /// Regex to strip date suffixes like -20250929 from model names.
    /// </summary>
    private static readonly Regex DateSuffixRegex = new(@"-\d{8}$", RegexOptions.Compiled);

    public static readonly DependencyProperty ModelDataProperty =
        DependencyProperty.Register(
            nameof(ModelData),
            typeof(Dictionary<string, long>),
            typeof(ModelUsageBar),
            new PropertyMetadata(null, OnModelDataChanged));

    /// <summary>
    /// Model name to token count mapping. Setting this property triggers bar recalculation.
    /// </summary>
    public Dictionary<string, long> ModelData
    {
        get => (Dictionary<string, long>)GetValue(ModelDataProperty);
        set => SetValue(ModelDataProperty, value);
    }

    /// <summary>
    /// Observable collection of computed bar items for the ItemsControl.
    /// </summary>
    public ObservableCollection<ModelBarItem> BarItems { get; } = new();

    public ModelUsageBar()
    {
        InitializeComponent();
    }

    private static void OnModelDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModelUsageBar control)
        {
            control.UpdateBars();
        }
    }

    /// <summary>
    /// Converts the ModelData dictionary into a sorted list of ModelBarItem objects
    /// and updates the BarItems collection.
    /// </summary>
    private void UpdateBars()
    {
        BarItems.Clear();

        if (ModelData is null || ModelData.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        long totalTokens = ModelData.Values.Sum();
        if (totalTokens == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        // Sort descending by token count so the largest model appears first.
        var sorted = ModelData
            .OrderByDescending(kv => kv.Value)
            .ToList();

        int colorIndex = 0;
        foreach (var (modelName, tokens) in sorted)
        {
            double percentage = (double)tokens / totalTokens * 100.0;
            if (percentage < 0.5)
                continue;

            var brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(Palette[colorIndex % Palette.Length]));
            brush.Freeze();

            BarItems.Add(new ModelBarItem
            {
                ModelName = ShortenModelName(modelName),
                Tokens = tokens,
                Percentage = percentage,
                BarBrush = brush,
                FormattedTokens = FormatTokenCount(tokens),
                FilledWidth = new GridLength(Math.Max(percentage, 1), GridUnitType.Star),
                EmptyWidth = new GridLength(Math.Max(100.0 - percentage, 0), GridUnitType.Star)
            });

            colorIndex++;
        }
    }

    /// <summary>
    /// Strips the "claude-" prefix and trailing date suffix for readability.
    /// Examples:
    ///   "claude-opus-4-6"             -> "opus-4-6"
    ///   "claude-sonnet-4-5-20250929"  -> "sonnet-4-5"
    /// </summary>
    internal static string ShortenModelName(string modelName)
    {
        string shortened = modelName;

        // Strip "claude-" prefix.
        if (shortened.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
        {
            shortened = shortened["claude-".Length..];
        }

        // Strip trailing date suffix (e.g., -20250929).
        shortened = DateSuffixRegex.Replace(shortened, string.Empty);

        return shortened;
    }

    /// <summary>
    /// Formats a token count into a human-readable string.
    /// Examples: 1234 -> "1.2K", 1234567 -> "1.2M", 500 -> "500"
    /// </summary>
    internal static string FormatTokenCount(long tokens)
    {
        return tokens switch
        {
            >= 1_000_000 => $"{tokens / 1_000_000.0:0.#}M tokens",
            >= 1_000 => $"{tokens / 1_000.0:0.#}K tokens",
            _ => $"{tokens:N0} tokens"
        };
    }
}

/// <summary>
/// Represents a single row in the model usage bar chart.
/// </summary>
public class ModelBarItem
{
    /// <summary>
    /// Shortened display name for the model (e.g., "opus-4-6").
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Raw token count for this model.
    /// </summary>
    public long Tokens { get; set; }

    /// <summary>
    /// Percentage of total tokens (0-100).
    /// </summary>
    public double Percentage { get; set; }

    /// <summary>
    /// Color brush for the bar segment.
    /// </summary>
    public SolidColorBrush BarBrush { get; set; } = Brushes.Gray;

    /// <summary>
    /// Human-readable token count (e.g., "85.4K tokens").
    /// </summary>
    public string FormattedTokens { get; set; } = string.Empty;

    /// <summary>
    /// Star-width GridLength for the filled portion of the bar.
    /// </summary>
    public GridLength FilledWidth { get; set; }

    /// <summary>
    /// Star-width GridLength for the empty portion of the bar.
    /// </summary>
    public GridLength EmptyWidth { get; set; }
}
