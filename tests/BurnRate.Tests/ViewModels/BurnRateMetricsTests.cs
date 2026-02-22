using BurnRate.Models;
using BurnRate.ViewModels;

namespace BurnRate.Tests.ViewModels;

/// <summary>
/// Unit tests for ProfileViewModel.ComputeBurnRateMetrics and FormatBurnRate.
/// </summary>
public class BurnRateMetricsTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static UsageSummary BuildSummary(
        long weeklyTokensUsed,
        long weeklyTokenLimit = 2_500_000,
        DateTime? weeklyResetsAt = null,
        int activeDays = 0)
    {
        var summary = new UsageSummary
        {
            WeeklyTokensUsed = weeklyTokensUsed,
            WeeklyTokenLimit = weeklyTokenLimit,
            WeeklyResetsAt = weeklyResetsAt
        };

        // Build DailyActivity with `activeDays` days of non-zero tokens
        var activity = new List<DailyActivitySummary>();
        var today = DateTime.UtcNow.Date;
        for (int i = 6; i >= 0; i--)
        {
            var tokens = (6 - i) < activeDays ? weeklyTokensUsed / Math.Max(1, activeDays) : 0;
            activity.Add(new DailyActivitySummary(today.AddDays(-i), 0, tokens));
        }
        summary.DailyActivity = activity;

        return summary;
    }

    // ── No data ───────────────────────────────────────────────────────────────

    [Fact]
    public void NoData_ZeroWeeklyTokens_BothDashes()
    {
        var summary = BuildSummary(weeklyTokensUsed: 0);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        Assert.Equal("—", summary.DailyBurnRateText);
        Assert.Equal("—", summary.RunwayText);
    }

    // ── FormatBurnRate ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(500, "500/day")]
    [InlineData(999, "999/day")]
    [InlineData(1_000, "1K/day")]
    [InlineData(1_500, "2K/day")]
    [InlineData(450_000, "450K/day")]
    [InlineData(1_000_000, "1.0M/day")]
    [InlineData(1_500_000, "1.5M/day")]
    [InlineData(2_000_000, "2.0M/day")]
    public void FormatBurnRate_CorrectSuffix(long tokensPerDay, string expected)
    {
        Assert.Equal(expected, ProfileViewModel.FormatBurnRate(tokensPerDay));
    }

    // ── Daily burn rate via WeeklyResetsAt ────────────────────────────────────

    [Fact]
    public void WithResetsAt_5DaysElapsed_DividesByFive()
    {
        // 5 days elapsed in the window (resets in 2 days)
        var resetsAt = DateTime.Now.AddDays(2);
        var summary = BuildSummary(weeklyTokensUsed: 500_000, weeklyResetsAt: resetsAt);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        // 500_000 / 5 = 100_000 → "100K/day"
        Assert.Equal("100K/day", summary.DailyBurnRateText);
    }

    [Fact]
    public void WithResetsAt_DaysElapsedLessThanOne_ClampsToOne()
    {
        // Window almost just reset (resets in 6.9 days → 0.1 elapsed → clamped to 1)
        var resetsAt = DateTime.Now.AddDays(6.9);
        var summary = BuildSummary(weeklyTokensUsed: 300_000, weeklyResetsAt: resetsAt);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        // 300_000 / 1 = 300_000 → "300K/day"
        Assert.Equal("300K/day", summary.DailyBurnRateText);
    }

    // ── Daily burn rate via DailyActivity fallback ────────────────────────────

    [Fact]
    public void NoResetsAt_3ActiveDays_DividesByThree()
    {
        // No WeeklyResetsAt, 3 days with activity
        var summary = BuildSummary(weeklyTokensUsed: 900_000, activeDays: 3);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        // 900_000 / 3 = 300_000 → "300K/day"
        Assert.Equal("300K/day", summary.DailyBurnRateText);
    }

    [Fact]
    public void NoResetsAt_NoActiveDays_ClampsToOne()
    {
        // No reset info, no activity days recorded yet (day 1 of usage)
        var summary = BuildSummary(weeklyTokensUsed: 200_000, activeDays: 0);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        // 200_000 / 1 = 200_000 → "200K/day"
        Assert.Equal("200K/day", summary.DailyBurnRateText);
    }

    // ── Runway ────────────────────────────────────────────────────────────────

    [Fact]
    public void Runway_ResetsBeforeLimit_ShowsResetsFirst()
    {
        // 3 days elapsed, 4 days remain. At 100K/day, runway = (2.5M - 300K) / 100K = 22 days.
        // 22 days > 4 days until reset → "Resets first"
        var resetsAt = DateTime.Now.AddDays(4);
        var summary = BuildSummary(weeklyTokensUsed: 300_000, weeklyTokenLimit: 2_500_000, weeklyResetsAt: resetsAt);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        Assert.Equal("Resets first", summary.RunwayText);
    }

    [Fact]
    public void Runway_LessThanOneDayLeft_ShowsLessThanOneDay()
    {
        // 2 days elapsed, reset in 5 days.
        // 2_400_000 used of 2_500_000 → remaining = 100_000
        // daily burn = 2_400_000 / 2 = 1_200_000/day
        // runway = 100_000 / 1_200_000 = 0.08 days < 1
        var resetsAt = DateTime.Now.AddDays(5);
        var summary = BuildSummary(weeklyTokensUsed: 2_400_000, weeklyTokenLimit: 2_500_000, weeklyResetsAt: resetsAt);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        Assert.Equal("< 1 day", summary.RunwayText);
    }

    [Fact]
    public void Runway_MultiDayRunway_ShowsApproximateDays()
    {
        // 5 days elapsed, reset in 2 days.
        // 1_000_000 used → daily burn = 200K/day
        // remaining = 2_500_000 - 1_000_000 = 1_500_000
        // runway = 1_500_000 / 200_000 = 7.5 days
        // 7.5 > 2 days until reset → "Resets first"
        // To get a numeric runway: put reset further away
        var resetsAt = DateTime.Now.AddDays(2);
        // With 2 days elapsed and burn = 500_000/day, runway = 1_000_000/500_000 = 2.0 days >= 2 → "Resets first"
        // Need runway < daysUntilReset. Let's use 7 days until reset, 3 elapsed, 250K/day burn, runway = 2_000_000/250K = 8 days > 7 → still resets first
        // Use very high burn: 6 days elapsed, reset in 1 day, 2_000_000 used
        // daily = 2_000_000/6 = 333K/day, remaining = 500_000, runway = 500_000/333K = 1.5 days > 1 day until reset → "Resets first"
        // Hmm, always resets first when reset is in 2 days and burn isn't extreme enough
        // Let me set reset in 10 days, 3 days elapsed (burn = 100K/day), remaining = 2.4M, runway = 24 days → resets first
        // To get a numeric runway I need reset far away. Use no WeeklyResetsAt:
        var noResetSummary = BuildSummary(
            weeklyTokensUsed: 750_000,
            weeklyTokenLimit: 2_500_000,
            weeklyResetsAt: null,
            activeDays: 3);
        // daily burn = 750_000 / 3 = 250_000/day, remaining = 1_750_000, runway = 7.0 days
        ProfileViewModel.ComputeBurnRateMetrics(noResetSummary);

        Assert.Equal("~7.0 days", noResetSummary.RunwayText);
    }

    [Fact]
    public void Runway_AtLimit_ShowsAtLimit()
    {
        var summary = BuildSummary(weeklyTokensUsed: 2_500_000, weeklyTokenLimit: 2_500_000, activeDays: 5);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        Assert.Equal("At limit", summary.RunwayText);
    }

    [Fact]
    public void Runway_OverLimit_ShowsAtLimit()
    {
        var summary = BuildSummary(weeklyTokensUsed: 3_000_000, weeklyTokenLimit: 2_500_000, activeDays: 5);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        Assert.Equal("At limit", summary.RunwayText);
    }

    [Fact]
    public void Runway_ZeroLimit_DashFallback()
    {
        var summary = BuildSummary(weeklyTokensUsed: 100_000, weeklyTokenLimit: 0, activeDays: 2);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        Assert.Equal("—", summary.RunwayText);
    }

    // ── Integration: RefreshAsync populates burn rate fields ──────────────────
    // These are tested indirectly via ProfileViewModelRefreshTests.
    // The fields DailyBurnRateText and RunwayText are set after every refresh.

    [Fact]
    public void AfterCompute_BurnRateText_IsNonEmptyWhenDataExists()
    {
        var summary = BuildSummary(weeklyTokensUsed: 500_000, weeklyTokenLimit: 2_500_000, activeDays: 3);

        ProfileViewModel.ComputeBurnRateMetrics(summary);

        Assert.NotEqual("—", summary.DailyBurnRateText);
        Assert.NotEmpty(summary.RunwayText);
    }

    // ── Session Runway ────────────────────────────────────────────────────────

    private static UsageSummary BuildSessionSummary(
        double sessionPercentage,
        double hoursUntilReset)
    {
        return new UsageSummary
        {
            SessionPercentage = sessionPercentage,
            SessionResetsAt = hoursUntilReset > 0
                ? DateTime.Now.AddHours(hoursUntilReset)
                : (DateTime?)null
        };
    }

    [Fact]
    public void SessionRunway_NoLiveData_ReturnsDash()
    {
        var summary = new UsageSummary { SessionPercentage = 0 };
        ProfileViewModel.ComputeSessionRunway(summary);
        Assert.Equal("—", summary.SessionRunwayText);
    }

    [Fact]
    public void SessionRunway_ZeroPercentage_ReturnsDash()
    {
        var summary = BuildSessionSummary(sessionPercentage: 0, hoursUntilReset: 3);
        ProfileViewModel.ComputeSessionRunway(summary);
        Assert.Equal("—", summary.SessionRunwayText);
    }

    [Fact]
    public void SessionRunway_AtLimit_ReturnsAtLimit()
    {
        // 99.5% or more = at limit
        var summary = BuildSessionSummary(sessionPercentage: 100, hoursUntilReset: 2);
        ProfileViewModel.ComputeSessionRunway(summary);
        Assert.Equal("At limit", summary.SessionRunwayText);
    }

    [Fact]
    public void SessionRunway_ResetsSooner_ReturnsResetsFirst()
    {
        // 2h elapsed (resets in 3h), 20% used → burn = 10%/h, remaining 80% → 8h to limit
        // 8h > 3h until reset → "Resets first"
        var summary = BuildSessionSummary(sessionPercentage: 20, hoursUntilReset: 3);
        ProfileViewModel.ComputeSessionRunway(summary);
        Assert.Equal("Resets first", summary.SessionRunwayText);
    }

    [Fact]
    public void SessionRunway_LimitReachedInMinutes_ShowsMinutes()
    {
        // 4h elapsed (resets in 1h), 80% used → burn = 20%/h, remaining 20% → 1h to limit
        // 1h >= 1h until reset → "Resets first"
        // Use higher burn: 4h elapsed, 96% used → burn = 24%/h, remaining 4% → 0.167h = 10m to limit
        // 0.167h < 1h until reset → show "~10m"
        var summary = BuildSessionSummary(sessionPercentage: 96, hoursUntilReset: 1);
        ProfileViewModel.ComputeSessionRunway(summary);
        Assert.Equal("~10m", summary.SessionRunwayText);
    }

    [Fact]
    public void SessionRunway_LimitReachedInHours_ShowsHoursAndMinutes()
    {
        // 1h elapsed (resets in 4h), 30% used → burn = 30%/h, remaining 70% → 2.333h = 2h 20m
        // 2.333h < 4h until reset → show "~2h 20m"
        var summary = BuildSessionSummary(sessionPercentage: 30, hoursUntilReset: 4);
        ProfileViewModel.ComputeSessionRunway(summary);
        Assert.Equal("~2h 20m", summary.SessionRunwayText);
    }

    [Fact]
    public void SessionRunway_LimitReachedInExactHours_OmitsMinutes()
    {
        // 1h elapsed (resets in 4h), 25% used → burn = 25%/h, remaining 75% → exactly 3h
        // 3h < 4h until reset → show "~3h"
        var summary = BuildSessionSummary(sessionPercentage: 25, hoursUntilReset: 4);
        ProfileViewModel.ComputeSessionRunway(summary);
        Assert.Equal("~3h", summary.SessionRunwayText);
    }

    [Fact]
    public void SessionRunway_JustReset_HoursElapsedNearZero_ExtrapolatesCorrectly()
    {
        // Reset in 4.999h → only 0.001h elapsed, 10% used → burn = 10,000%/h
        // remaining 90% / 10,000%/h = 0.009h < 1m → "< 1m"
        var summary = BuildSessionSummary(sessionPercentage: 10, hoursUntilReset: 4.999);
        ProfileViewModel.ComputeSessionRunway(summary);
        Assert.Equal("< 1m", summary.SessionRunwayText);
    }
}
