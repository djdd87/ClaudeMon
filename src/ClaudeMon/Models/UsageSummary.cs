using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeMon.Models;

/// <summary>
/// Computed model for UI binding. Aggregates data from StatsCache, CredentialsInfo,
/// and SessionMeta into a single view-friendly summary of Claude Code usage.
/// </summary>
public partial class UsageSummary : ObservableObject
{
    /// <summary>
    /// Estimated percentage of the weekly rate limit consumed (0-100).
    /// </summary>
    [ObservableProperty]
    private double _estimatedPercentage;

    /// <summary>
    /// Total tokens used in the current weekly billing window.
    /// </summary>
    [ObservableProperty]
    private long _weeklyTokensUsed;

    /// <summary>
    /// Estimated weekly token limit based on the rate limit tier.
    /// </summary>
    [ObservableProperty]
    private long _weeklyTokenLimit;

    /// <summary>
    /// Rate limit tier identifier (e.g. "default_claude_max_5x").
    /// </summary>
    [ObservableProperty]
    private string _rateLimitTier = string.Empty;

    /// <summary>
    /// Subscription type (e.g. "team", "pro", "max").
    /// </summary>
    [ObservableProperty]
    private string _subscriptionType = string.Empty;

    /// <summary>
    /// Number of messages sent today.
    /// </summary>
    [ObservableProperty]
    private int _todayMessages;

    /// <summary>
    /// Total tokens used today (input + output across all models).
    /// </summary>
    [ObservableProperty]
    private long _todayTokens;

    /// <summary>
    /// Number of sessions started today.
    /// </summary>
    [ObservableProperty]
    private int _todaySessions;

    /// <summary>
    /// Lifetime total number of sessions.
    /// </summary>
    [ObservableProperty]
    private int _totalSessions;

    /// <summary>
    /// Lifetime total number of messages.
    /// </summary>
    [ObservableProperty]
    private int _totalMessages;

    /// <summary>
    /// Token usage by model name for the last 7 days.
    /// </summary>
    [ObservableProperty]
    private Dictionary<string, long> _modelBreakdown = new();

    /// <summary>
    /// Daily activity entries for the last 7 days (date, message count, token count).
    /// </summary>
    [ObservableProperty]
    private List<DailyActivitySummary> _dailyActivity = [];

    /// <summary>
    /// The date of the most recent data point in stats-cache.json.
    /// </summary>
    [ObservableProperty]
    private DateTime? _lastDataDate;

    /// <summary>
    /// Timestamp of the last successful data refresh from disk.
    /// </summary>
    [ObservableProperty]
    private DateTime _lastRefreshTime;

    // ── Live API data ──

    /// <summary>
    /// Lifetime estimated API-equivalent cost in USD, summed across all models.
    /// </summary>
    [ObservableProperty]
    private double _estimatedCostUsd;

    /// <summary>
    /// Total speculative pre-execution time saved, formatted for display (e.g. "4.2h", "37m").
    /// </summary>
    [ObservableProperty]
    private string _timeSavedFormatted = "—";

    /// <summary>
    /// Whether live API data is available (vs. estimated from local files).
    /// </summary>
    [ObservableProperty]
    private bool _isLive;

    /// <summary>
    /// 5-hour session utilization (0-100). From live API.
    /// </summary>
    [ObservableProperty]
    private double _sessionPercentage;

    /// <summary>
    /// When the 5-hour session window resets.
    /// </summary>
    [ObservableProperty]
    private DateTime? _sessionResetsAt;

    /// <summary>
    /// 7-day weekly utilization (0-100). From live API.
    /// </summary>
    [ObservableProperty]
    private double _weeklyPercentage;

    /// <summary>
    /// When the 7-day window resets.
    /// </summary>
    [ObservableProperty]
    private DateTime? _weeklyResetsAt;

    /// <summary>
    /// Copies all property values from another summary into this instance.
    /// Fires PropertyChanged for each changed property, keeping WPF bindings in sync.
    /// </summary>
    public void UpdateFrom(UsageSummary other)
    {
        EstimatedPercentage = other.EstimatedPercentage;
        WeeklyTokensUsed = other.WeeklyTokensUsed;
        WeeklyTokenLimit = other.WeeklyTokenLimit;
        RateLimitTier = other.RateLimitTier;
        SubscriptionType = other.SubscriptionType;
        TodayMessages = other.TodayMessages;
        TodayTokens = other.TodayTokens;
        TodaySessions = other.TodaySessions;
        TotalSessions = other.TotalSessions;
        TotalMessages = other.TotalMessages;
        ModelBreakdown = other.ModelBreakdown;
        DailyActivity = other.DailyActivity;
        LastDataDate = other.LastDataDate;
        LastRefreshTime = other.LastRefreshTime;
        EstimatedCostUsd = other.EstimatedCostUsd;
        TimeSavedFormatted = other.TimeSavedFormatted;
        IsLive = other.IsLive;
        SessionPercentage = other.SessionPercentage;
        SessionResetsAt = other.SessionResetsAt;
        WeeklyPercentage = other.WeeklyPercentage;
        WeeklyResetsAt = other.WeeklyResetsAt;
    }
}

/// <summary>
/// Lightweight record for daily activity suitable for chart/list binding.
/// </summary>
public sealed record DailyActivitySummary(DateTime Date, int Messages, long Tokens);
