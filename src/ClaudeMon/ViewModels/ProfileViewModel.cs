using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Hardcodet.Wpf.TaskbarNotification;
using ClaudeMon.Models;
using ClaudeMon.Services;

namespace ClaudeMon.ViewModels;

public partial class ProfileViewModel : ObservableObject, IDisposable
{
    private readonly ClaudeDataService _dataService;
    private readonly UsageCalculator _calculator;
    private readonly TrayIconService _iconService;
    private readonly FileWatcherService _watcherService;
    private readonly LiveUsageService _liveUsageService;
    private readonly ThemeService _themeService;
    private TaskbarIcon? _trayIcon;
    private bool _disposed;

    [ObservableProperty]
    private string _profileName;

    [ObservableProperty]
    private string _profilePath;

    [ObservableProperty]
    private UsageSummary _usage = new();

    [ObservableProperty]
    private string _tooltipText = "Loading...";

    [ObservableProperty]
    private string _planDisplayName = "Unknown Plan";

    [ObservableProperty]
    private System.Windows.Media.ImageSource? _gaugeImageSource;

    public event Action<ProfileViewModel>? TrayLeftClicked;

    // Cache for WPF BitmapImages keyed by absolute file path (UI thread only).
    private static readonly Dictionary<string, BitmapImage> _gaugeImageCache = [];

    private static readonly Dictionary<(string Tier, string Sub), string> KnownPlans = new()
    {
        [("default_claude_max_5x", "team")] = "Team Premium",
        [("default_claude_max_20x", "team")] = "Team Premium 20x",
        [("default_claude_max_5x", "")] = "Max 5x",
        [("default_claude_max_20x", "")] = "Max 20x",
        [("pro", "")] = "Pro",
        [("pro", "team")] = "Team Pro",
        [("default_claude_ai", "pro")] = "Pro",
        [("default_claude_ai", "")] = "Pro",
        [("default_claude_ai", "team")] = "Team Pro",
        [("default_raven", "team")] = "Team Standard",
        [("default_raven", "")] = "Standard",
    };

    public ProfileViewModel(
        ProfileConfig config,
        UsageCalculator calculator,
        ThemeService themeService,
        int refreshIntervalSeconds = 60)
    {
        _profileName = config.Name;
        _profilePath = config.Path;
        _dataService = new ClaudeDataService(config.Path);
        _calculator = calculator;
        _themeService = themeService;
        _iconService = new TrayIconService();
        _watcherService = new FileWatcherService(config.Path, refreshIntervalSeconds * 1000);
        _liveUsageService = new LiveUsageService(config.Path);
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = $"{ProfileName} - Loading...",
            Icon = _iconService.CreateNotifyIcon(-1)
        };
        _trayIcon.TrayLeftMouseUp += (_, _) => TrayLeftClicked?.Invoke(this);

        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = $"Open {ProfileName}" };
        openItem.Click += (_, _) => TrayLeftClicked?.Invoke(this);
        menu.Items.Add(openItem);

        var refreshItem = new System.Windows.Controls.MenuItem { Header = "Refresh" };
        refreshItem.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu = menu;

        _watcherService.DataChanged += OnDataChanged;
        _watcherService.Start();

        _themeService.ThemeChanged += OnThemeChanged;

        _ = RefreshAsync();
    }

    /// <summary>
    /// Re-renders the tray icon and gauge image without reloading data.
    /// Called after a custom theme is selected.
    /// </summary>
    public void RefreshTrayIcon()
    {
        if (_trayIcon != null)
        {
            var iconPct = Usage.IsLive ? Usage.SessionPercentage : Usage.EstimatedPercentage;
            _trayIcon.Icon = _iconService.CreateNotifyIcon(iconPct);
        }
        UpdateGaugeImage();
    }

    public async Task RefreshAsync()
    {
        try
        {
            // Load local stats for detailed breakdowns (messages, model usage, activity chart)
            var stats = await _dataService.GetStatsCacheAsync();
            var creds = await _dataService.GetCredentialsAsync();
            var summary = _calculator.Calculate(stats, creds);

            // stats-cache.json is only recomputed periodically by Claude Code, so recent
            // days can be stale or missing. Read directly from JSONL files and use those
            // counts wherever they show more activity than the cached data.
            var jsonlStats = await _dataService.GetRecentJsonlStatsAsync(7);
            var todayUtc = DateTime.UtcNow.Date;

            if (jsonlStats.TryGetValue(todayUtc, out var todayJsonl))
            {
                summary.TodayMessages = todayJsonl.Messages;
                summary.TodayTokens = todayJsonl.OutputTokens;
                summary.TodaySessions = todayJsonl.Sessions;
            }

            // stats-cache dailyModelTokens is only updated periodically, so the weekly
            // total can lag badly (e.g. missing several days). Use the JSONL 7-day sum
            // instead, which is always current.
            var jsonlWeeklyTokens = jsonlStats.Values.Sum(v => v.OutputTokens);
            if (jsonlWeeklyTokens > summary.WeeklyTokensUsed)
                summary.WeeklyTokensUsed = jsonlWeeklyTokens;

            // Build model breakdown from JSONL data (always live) and use it
            // instead of the potentially stale stats-cache model breakdown.
            var jsonlModelBreakdown = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var dayStats in jsonlStats.Values)
            {
                if (dayStats.TokensByModel is null) continue;
                foreach (var (model, tokens) in dayStats.TokensByModel)
                {
                    jsonlModelBreakdown[model] = jsonlModelBreakdown.GetValueOrDefault(model) + tokens;
                }
            }
            if (jsonlModelBreakdown.Count > 0)
                summary.ModelBreakdown = jsonlModelBreakdown;

            summary.DailyActivity = summary.DailyActivity.Select(day =>
            {
                if (jsonlStats.TryGetValue(day.Date, out var jsonlDay) && jsonlDay.Messages > day.Messages)
                    return new DailyActivitySummary(day.Date, jsonlDay.Messages, jsonlDay.OutputTokens);
                return day;
            }).ToList();

            // Try live API for accurate percentages (isolated so local stats still work on failure)
            try
            {
                var liveUsage = await _liveUsageService.GetUsageAsync();
                if (liveUsage != null)
                {
                    summary.IsLive = true;

                    if (liveUsage.FiveHour != null)
                    {
                        summary.SessionPercentage = liveUsage.FiveHour.Utilization;
                        System.Diagnostics.Debug.WriteLine(
                            $"[{ProfileName}] Live 5h: {liveUsage.FiveHour.Utilization:F1}%, resets: {liveUsage.FiveHour.ResetsAt}");
                        if (DateTime.TryParse(liveUsage.FiveHour.ResetsAt, null,
                                System.Globalization.DateTimeStyles.RoundtripKind, out var sessionReset))
                            summary.SessionResetsAt = sessionReset.ToLocalTime();
                    }

                    if (liveUsage.SevenDay != null)
                    {
                        summary.WeeklyPercentage = liveUsage.SevenDay.Utilization;
                        System.Diagnostics.Debug.WriteLine(
                            $"[{ProfileName}] Live 7d: {liveUsage.SevenDay.Utilization:F1}%, resets: {liveUsage.SevenDay.ResetsAt}");
                        if (DateTime.TryParse(liveUsage.SevenDay.ResetsAt, null,
                                System.Globalization.DateTimeStyles.RoundtripKind, out var weeklyReset))
                            summary.WeeklyResetsAt = weeklyReset.ToLocalTime();
                    }

                    // Use the live session percentage as the primary gauge/icon metric
                    summary.EstimatedPercentage = summary.SessionPercentage;
                }
            }
            catch (Exception liveEx)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[{ProfileName}] Live API failed (using estimates): {liveEx.Message}");
            }

            ComputeBurnRateMetrics(summary);
            Usage.UpdateFrom(summary);

            // Format tooltip
            var pctText = Usage.IsLive
                ? FormatPct(Usage.WeeklyPercentage)
                : (Usage.EstimatedPercentage >= 0
                    ? $"~{FormatPct(Usage.EstimatedPercentage)} (Est.)"
                    : "Unknown");

            PlanDisplayName = FormatTier(Usage.RateLimitTier, Usage.SubscriptionType);
            TooltipText = Usage.IsLive
                ? $"{ProfileName} - {FormatPct(Usage.SessionPercentage)} session | {FormatPct(Usage.WeeklyPercentage)} weekly | {PlanDisplayName}"
                : $"{ProfileName} - {pctText} | {PlanDisplayName}";

            if (_trayIcon != null)
            {
                _trayIcon.ToolTipText = TooltipText;
                var iconPct = Usage.IsLive
                    ? Usage.SessionPercentage
                    : Usage.EstimatedPercentage;
                _trayIcon.Icon = _iconService.CreateNotifyIcon(iconPct);
            }

            UpdateGaugeImage();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{ProfileName}] Refresh error: {ex.Message}");
            TooltipText = $"{ProfileName} - Error loading data";
        }
    }

    private void UpdateGaugeImage()
    {
        var customTheme = _themeService.ActiveCustomTheme;
        if (customTheme?.FaceImages is not { Count: > 0 })
        {
            GaugeImageSource = null;
            return;
        }

        var pct = Usage.IsLive ? Usage.SessionPercentage : Usage.EstimatedPercentage;
        var path = customTheme.ResolveFacePath(pct < 0 ? 0 : pct);
        if (path == null)
        {
            GaugeImageSource = null;
            return;
        }

        if (!_gaugeImageCache.TryGetValue(path, out var bmp))
        {
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _gaugeImageCache[path] = bmp;
        }

        GaugeImageSource = bmp;
    }

    private void OnDataChanged()
    {
        Application.Current?.Dispatcher.InvokeAsync(async () => await RefreshAsync());
    }

    private void OnThemeChanged(AppThemeMode _)
    {
        Application.Current?.Dispatcher.InvokeAsync(async () => await RefreshAsync());
    }

    internal static void ComputeBurnRateMetrics(UsageSummary summary)
    {
        double daysElapsed;
        if (summary.WeeklyResetsAt.HasValue)
        {
            // Window is 7 days; elapsed = 7 minus how many days remain
            daysElapsed = Math.Max(1.0, 7.0 - (summary.WeeklyResetsAt.Value - DateTime.Now).TotalDays);
        }
        else
        {
            // Fallback: count days that had any token activity
            daysElapsed = Math.Max(1.0, summary.DailyActivity.Count(d => d.Tokens > 0));
        }

        if (summary.WeeklyTokensUsed <= 0)
        {
            summary.DailyBurnRateText = "—";
            summary.RunwayText = "—";
            return;
        }

        long dailyBurnRate = (long)(summary.WeeklyTokensUsed / daysElapsed);
        summary.DailyBurnRateText = FormatBurnRate(dailyBurnRate);

        if (dailyBurnRate <= 0 || summary.WeeklyTokenLimit <= 0)
        {
            summary.RunwayText = "—";
            return;
        }

        long remaining = summary.WeeklyTokenLimit - summary.WeeklyTokensUsed;
        if (remaining <= 0)
        {
            summary.RunwayText = "At limit";
            return;
        }

        double runwayDays = remaining / (double)dailyBurnRate;

        if (summary.WeeklyResetsAt.HasValue)
        {
            double daysUntilReset = (summary.WeeklyResetsAt.Value - DateTime.Now).TotalDays;
            if (runwayDays >= daysUntilReset)
            {
                summary.RunwayText = "Resets first";
                return;
            }
        }

        summary.RunwayText = runwayDays < 1.0 ? "< 1 day" : $"~{runwayDays:F1} days";
    }

    internal static string FormatBurnRate(long tokensPerDay)
    {
        if (tokensPerDay >= 1_000_000)
            return $"{tokensPerDay / 1_000_000.0:F1}M/day";
        if (tokensPerDay >= 1_000)
            return $"{tokensPerDay / 1_000.0:F0}K/day";
        return $"{tokensPerDay}/day";
    }

    private static string FormatPct(double percentage) =>
        percentage >= 99.5 ? "Limit" : $"{percentage:F0}%";

    internal static string FormatTier(string? tier, string? subscription)
    {
        var t = tier ?? "";
        var s = subscription ?? "";

        if (KnownPlans.TryGetValue((t, s), out var name))
            return name;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(s))
            parts.Add(char.ToUpper(s[0]) + s[1..]);
        if (!string.IsNullOrEmpty(t))
        {
            var display = t.Replace("default_claude_", "").Replace("default_", "").Replace("_", " ").Trim();
            if (display.Length > 0)
                parts.Add(char.ToUpper(display[0]) + display[1..]);
        }
        return parts.Count > 0 ? string.Join(" ", parts) : "Unknown Plan";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _themeService.ThemeChanged -= OnThemeChanged;
        _watcherService.DataChanged -= OnDataChanged;
        _watcherService.Dispose();
        _iconService.Dispose();
        _liveUsageService.Dispose();

        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}
