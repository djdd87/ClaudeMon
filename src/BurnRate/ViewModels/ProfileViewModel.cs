using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Hardcodet.Wpf.TaskbarNotification;
using BurnRate.Models;
using BurnRate.Services;

namespace BurnRate.ViewModels;

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
    private MainViewModel? _mainVm;
    private System.ComponentModel.PropertyChangedEventHandler? _mainVmPropertyChanged;
    private readonly System.Threading.SemaphoreSlim _refreshLock = new(1, 1);

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

    public ObservableCollection<MetricCardViewModel> ActiveMetrics { get; } = [];

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

    public void Initialize(MainViewModel mainVm)
    {
        _mainVm = mainVm;

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

        // --- Theme submenu ---
        var themeMenu = new System.Windows.Controls.MenuItem { Header = "Theme" };

        var darkItem   = new System.Windows.Controls.MenuItem { Header = "Dark",   IsCheckable = true };
        var lightItem  = new System.Windows.Controls.MenuItem { Header = "Light",  IsCheckable = true };
        var systemItem = new System.Windows.Controls.MenuItem { Header = "System", IsCheckable = true };

        darkItem.Click   += (_, _) => mainVm.ThemeMode = AppThemeMode.Dark;
        lightItem.Click  += (_, _) => mainVm.ThemeMode = AppThemeMode.Light;
        systemItem.Click += (_, _) => mainVm.ThemeMode = AppThemeMode.System;

        themeMenu.Items.Add(darkItem);
        themeMenu.Items.Add(lightItem);
        themeMenu.Items.Add(systemItem);

        var customItems = new List<(System.Windows.Controls.MenuItem Item, CustomTheme Theme)>();
        foreach (var ct in mainVm.AvailableCustomThemes)
        {
            var capturedCt = ct;
            var ctItem = new System.Windows.Controls.MenuItem { Header = ct.DisplayName, IsCheckable = true };
            ctItem.Click += (_, _) => mainVm.SelectCustomThemeCommand.Execute(capturedCt);
            themeMenu.Items.Add(ctItem);
            customItems.Add((ctItem, capturedCt));
        }

        void UpdateThemeChecks()
        {
            darkItem.IsChecked   = mainVm.ThemeMode == AppThemeMode.Dark;
            lightItem.IsChecked  = mainVm.ThemeMode == AppThemeMode.Light;
            systemItem.IsChecked = mainVm.ThemeMode == AppThemeMode.System;
            foreach (var (item, ct) in customItems)
                item.IsChecked = mainVm.ThemeMode == AppThemeMode.Custom
                    && mainVm.ActiveCustomTheme?.Id == ct.Id;
        }

        UpdateThemeChecks();

        // --- Metrics manager ---
        var manageMetricsItem = new System.Windows.Controls.MenuItem { Header = "Manage Metrics..." };
        manageMetricsItem.Click += (_, _) => mainVm.OpenMetricsManagerCommand.Execute(null);

        _mainVmPropertyChanged = (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.ThemeMode) or nameof(MainViewModel.ActiveCustomTheme))
                Application.Current?.Dispatcher.Invoke(UpdateThemeChecks);
            if (args.PropertyName == nameof(MainViewModel.EnabledMetricIds))
                Application.Current?.Dispatcher.Invoke(() => RebuildMetrics(mainVm.EnabledMetricIds));
        };
        mainVm.PropertyChanged += _mainVmPropertyChanged;

        menu.Items.Add(themeMenu);
        menu.Items.Add(manageMetricsItem);
        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu = menu;

        _watcherService.DataChanged += OnDataChanged;
        _watcherService.Start();

        _themeService.ThemeChanged += OnThemeChanged;

        RebuildMetrics(mainVm.EnabledMetricIds);

        _ = RefreshAsync();
    }

    private void RebuildMetrics(IEnumerable<string> enabledIds)
    {
        ActiveMetrics.Clear();
        foreach (var id in enabledIds)
        {
            if (id == "UsageLimits") continue; // section toggle, not a card
            var def = MetricRegistry.Find(id);
            if (def != null)
                ActiveMetrics.Add(new MetricCardViewModel(def.Label, def.Hint, def.GetValue));
        }
        RefreshMetricValues();
    }

    private void RefreshMetricValues()
    {
        foreach (var metric in ActiveMetrics)
            metric.Refresh(Usage);
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
        if (!await _refreshLock.WaitAsync(0))
            return; // A refresh is already in progress; skip this one.

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

                        // The plan limits in appsettings.json are approximations. When the live API
                        // reports a weekly utilization percentage, back-calculate the real effective
                        // limit so that runway is computed against what Anthropic actually measures,
                        // not a potentially stale/wrong configured value.
                        if (liveUsage.SevenDay.Utilization > 0 && summary.WeeklyTokensUsed > 0)
                            summary.WeeklyTokenLimit = (long)Math.Round(
                                summary.WeeklyTokensUsed / (liveUsage.SevenDay.Utilization / 100.0));
                    }

                    if (liveUsage.SevenDaySonnet != null)
                    {
                        summary.SonnetPercentage = liveUsage.SevenDaySonnet.Utilization;
                        System.Diagnostics.Debug.WriteLine(
                            $"[{ProfileName}] Live 7d Sonnet: {liveUsage.SevenDaySonnet.Utilization:F1}%, resets: {liveUsage.SevenDaySonnet.ResetsAt}");
                        if (DateTime.TryParse(liveUsage.SevenDaySonnet.ResetsAt, null,
                                System.Globalization.DateTimeStyles.RoundtripKind, out var sonnetReset))
                            summary.SonnetResetsAt = sonnetReset.ToLocalTime();
                    }

                    if (liveUsage.ExtraUsage != null)
                    {
                        summary.ExtraUsageEnabled = liveUsage.ExtraUsage.IsEnabled == true;
                        summary.ExtraUsagePercentage = liveUsage.ExtraUsage.Utilization ?? -1;
                        summary.ExtraUsageUsed = liveUsage.ExtraUsage.UsedCredits ?? 0;
                        summary.ExtraUsageLimit = liveUsage.ExtraUsage.MonthlyLimit ?? 0;
                        summary.ExtraUsageCurrency = liveUsage.ExtraUsage.Currency ?? "USD";
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
            ComputeSessionRunway(summary);
            Usage.UpdateFrom(summary);
            RefreshMetricValues();

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
        finally
        {
            _refreshLock.Release();
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

    internal static void ComputeSessionRunway(UsageSummary summary)
    {
        if (!summary.SessionResetsAt.HasValue || summary.SessionPercentage <= 0)
        {
            summary.SessionRunwayText = "—";
            return;
        }

        double hoursUntilReset = (summary.SessionResetsAt.Value - DateTime.Now).TotalHours;
        if (hoursUntilReset <= 0)
        {
            summary.SessionRunwayText = "—";
            return;
        }

        const double sessionWindowHours = 5.0;
        double hoursElapsed = sessionWindowHours - hoursUntilReset;
        if (hoursElapsed <= 0)
        {
            summary.SessionRunwayText = "—";
            return;
        }

        if (summary.SessionPercentage >= 99.5)
        {
            summary.SessionRunwayText = "At limit";
            return;
        }

        double burnRatePerHour = summary.SessionPercentage / hoursElapsed;
        double remaining = 100.0 - summary.SessionPercentage;
        double hoursUntilLimit = remaining / burnRatePerHour;

        if (hoursUntilLimit >= hoursUntilReset)
        {
            summary.SessionRunwayText = "Resets first";
            return;
        }

        if (hoursUntilLimit < 1.0 / 60.0)
            summary.SessionRunwayText = "< 1m";
        else if (hoursUntilLimit < 1.0)
            summary.SessionRunwayText = $"~{(int)(hoursUntilLimit * 60)}m";
        else
        {
            int hours = (int)hoursUntilLimit;
            int mins = (int)((hoursUntilLimit - hours) * 60);
            summary.SessionRunwayText = mins > 0 ? $"~{hours}h {mins}m" : $"~{hours}h";
        }
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

        if (_mainVm != null && _mainVmPropertyChanged != null)
            _mainVm.PropertyChanged -= _mainVmPropertyChanged;

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

        _refreshLock.Dispose();
    }
}
