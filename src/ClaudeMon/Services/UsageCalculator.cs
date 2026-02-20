using ClaudeMon.Models;

namespace ClaudeMon.Services;

/// <summary>
/// Calculates an estimated usage percentage and aggregated metrics from
/// raw stats-cache data and plan/tier information.
/// </summary>
public sealed class UsageCalculator
{
    private const long DefaultWeeklyLimit = 2_500_000;

    private readonly Dictionary<string, long> _planLimits;

    public UsageCalculator(Dictionary<string, long> planLimits)
    {
        _planLimits = planLimits ?? throw new ArgumentNullException(nameof(planLimits));
    }

    /// <summary>
    /// Builds a <see cref="UsageSummary"/> from the raw stats-cache data and
    /// credential/tier information.
    /// </summary>
    public UsageSummary Calculate(StatsCache? stats, ClaudeAiOAuthInfo? creds)
    {
        var summary = new UsageSummary();

        // --- Tier / subscription metadata ---
        var tier = creds?.RateLimitTier ?? string.Empty;
        var subscription = creds?.SubscriptionType ?? string.Empty;
        summary.RateLimitTier = tier;
        summary.SubscriptionType = subscription;

        // --- Weekly token limit from plan tier ---
        long weeklyLimit = _planLimits.TryGetValue(tier, out var limit)
            ? limit
            : DefaultWeeklyLimit;
        summary.WeeklyTokenLimit = weeklyLimit;

        if (stats is null)
        {
            summary.EstimatedPercentage = -1; // signals "no data"
            summary.LastRefreshTime = DateTime.Now;
            return summary;
        }

        // --- Last data date ---
        if (DateTime.TryParse(stats.LastComputedDate, out var lastDate))
            summary.LastDataDate = lastDate;

        // --- Lifetime totals ---
        summary.TotalSessions = stats.TotalSessions;
        summary.TotalMessages = stats.TotalMessages;

        // --- Determine the 7-day rolling window ---
        var today = DateTime.UtcNow.Date;
        var windowStart = today.AddDays(-6); // inclusive, so 7 days total

        // --- Weekly tokens: sum dailyModelTokens within the window ---
        long weeklyTokens = 0;
        var modelTotals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in stats.DailyModelTokens)
        {
            if (!DateTime.TryParse(entry.Date, out var entryDate))
                continue;

            if (entryDate.Date < windowStart || entryDate.Date > today)
                continue;

            foreach (var (model, tokens) in entry.TokensByModel)
            {
                weeklyTokens += tokens;

                if (modelTotals.TryGetValue(model, out var existing))
                    modelTotals[model] = existing + tokens;
                else
                    modelTotals[model] = tokens;
            }
        }

        summary.WeeklyTokensUsed = weeklyTokens;
        summary.ModelBreakdown = modelTotals;

        // --- Percentage (clamped 0-100) ---
        double percentage = weeklyLimit > 0
            ? (weeklyTokens / (double)weeklyLimit) * 100.0
            : 0.0;
        summary.EstimatedPercentage = Math.Clamp(percentage, 0.0, 100.0);

        // --- Today's stats from dailyActivity ---
        var todayStr = today.ToString("yyyy-MM-dd");

        var todayActivity = stats.DailyActivity
            .FirstOrDefault(a => a.Date == todayStr);
        if (todayActivity is not null)
        {
            summary.TodayMessages = todayActivity.MessageCount;
            summary.TodaySessions = todayActivity.SessionCount;
        }

        // --- Today's tokens from dailyModelTokens ---
        var todayTokenEntry = stats.DailyModelTokens
            .FirstOrDefault(t => t.Date == todayStr);
        if (todayTokenEntry is not null)
        {
            summary.TodayTokens = todayTokenEntry.TokensByModel.Values.Sum();
        }

        // --- Daily activity for the last 7 calendar days (fill missing with 0) ---
        var dailyList = new List<DailyActivitySummary>();
        for (int i = 6; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var dateStr = date.ToString("yyyy-MM-dd");

            int messages = stats.DailyActivity
                .FirstOrDefault(a => a.Date == dateStr)?.MessageCount ?? 0;

            long tokens = stats.DailyModelTokens
                .FirstOrDefault(t => t.Date == dateStr)?
                .TokensByModel.Values.Sum() ?? 0;

            dailyList.Add(new DailyActivitySummary(date, messages, tokens));
        }
        summary.DailyActivity = dailyList;

        summary.LastRefreshTime = DateTime.Now;

        return summary;
    }
}
