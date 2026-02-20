using ClaudeMon.Models;
using ClaudeMon.Services;

namespace ClaudeMon.Tests.Services;

public class UsageCalculatorTests
{
    private static readonly Dictionary<string, long> DefaultLimits = new()
    {
        ["default_claude_max_5x"] = 450_000,
        ["default_claude_max_20x"] = 1_800_000,
        ["pro"] = 100_000,
    };

    private UsageCalculator CreateCalculator(Dictionary<string, long>? limits = null)
        => new(limits ?? DefaultLimits);

    [Fact]
    public void NullStats_ReturnsNoData()
    {
        var calc = CreateCalculator();
        var result = calc.Calculate(null, null);

        Assert.Equal(-1, result.EstimatedPercentage);
        Assert.Equal(0, result.TodayMessages);
        Assert.Equal(0, result.TodayTokens);
        Assert.Equal(0, result.TodaySessions);
        Assert.Equal(0, result.WeeklyTokensUsed);
    }

    [Fact]
    public void NullCreds_EmptyTierAndSubscription()
    {
        var stats = new StatsCache();
        var calc = CreateCalculator();
        var result = calc.Calculate(stats, null);

        Assert.Equal(string.Empty, result.RateLimitTier);
        Assert.Equal(string.Empty, result.SubscriptionType);
    }

    [Fact]
    public void KnownTier_UsesConfiguredLimit()
    {
        var creds = new ClaudeAiOAuthInfo { RateLimitTier = "default_claude_max_5x" };
        var stats = new StatsCache();
        var calc = CreateCalculator();
        var result = calc.Calculate(stats, creds);

        Assert.Equal(450_000, result.WeeklyTokenLimit);
    }

    [Fact]
    public void UnknownTier_FallsBackToDefault()
    {
        var creds = new ClaudeAiOAuthInfo { RateLimitTier = "some_unknown_tier" };
        var stats = new StatsCache();
        var calc = CreateCalculator();
        var result = calc.Calculate(stats, creds);

        Assert.Equal(2_500_000, result.WeeklyTokenLimit);
    }

    [Fact]
    public void EntriesOutsideWindow_ExcludedFromWeeklySum()
    {
        var today = DateTime.UtcNow.Date;
        var stats = new StatsCache
        {
            DailyModelTokens =
            [
                new DailyModelTokensEntry
                {
                    Date = today.AddDays(-10).ToString("yyyy-MM-dd"),
                    TokensByModel = new() { ["claude-sonnet"] = 50_000 }
                },
                new DailyModelTokensEntry
                {
                    Date = today.ToString("yyyy-MM-dd"),
                    TokensByModel = new() { ["claude-sonnet"] = 10_000 }
                }
            ]
        };

        var calc = CreateCalculator();
        var result = calc.Calculate(stats, null);

        Assert.Equal(10_000, result.WeeklyTokensUsed);
    }

    [Fact]
    public void Percentage_ClampedAt100()
    {
        var today = DateTime.UtcNow.Date;
        var stats = new StatsCache
        {
            DailyModelTokens =
            [
                new DailyModelTokensEntry
                {
                    Date = today.ToString("yyyy-MM-dd"),
                    TokensByModel = new() { ["claude-sonnet"] = 5_000_000 }
                }
            ]
        };

        var calc = CreateCalculator();
        var result = calc.Calculate(stats, null);

        Assert.Equal(100.0, result.EstimatedPercentage);
    }

    [Fact]
    public void ZeroWeeklyLimit_ReturnsZeroPercent()
    {
        var today = DateTime.UtcNow.Date;
        var limits = new Dictionary<string, long> { ["test_tier"] = 0 };
        var creds = new ClaudeAiOAuthInfo { RateLimitTier = "test_tier" };
        var stats = new StatsCache
        {
            DailyModelTokens =
            [
                new DailyModelTokensEntry
                {
                    Date = today.ToString("yyyy-MM-dd"),
                    TokensByModel = new() { ["claude-sonnet"] = 10_000 }
                }
            ]
        };

        var calc = new UsageCalculator(limits);
        var result = calc.Calculate(stats, creds);

        Assert.Equal(0.0, result.EstimatedPercentage);
    }

    [Fact]
    public void ModelBreakdown_AggregatesAcrossMultipleDays()
    {
        var today = DateTime.UtcNow.Date;
        var stats = new StatsCache
        {
            DailyModelTokens =
            [
                new DailyModelTokensEntry
                {
                    Date = today.AddDays(-1).ToString("yyyy-MM-dd"),
                    TokensByModel = new() { ["claude-sonnet"] = 10_000, ["claude-opus"] = 5_000 }
                },
                new DailyModelTokensEntry
                {
                    Date = today.ToString("yyyy-MM-dd"),
                    TokensByModel = new() { ["claude-sonnet"] = 20_000 }
                }
            ]
        };

        var calc = CreateCalculator();
        var result = calc.Calculate(stats, null);

        Assert.Equal(30_000, result.ModelBreakdown["claude-sonnet"]);
        Assert.Equal(5_000, result.ModelBreakdown["claude-opus"]);
    }

    [Fact]
    public void TodayStats_FromMatchingDateEntries()
    {
        var today = DateTime.UtcNow.Date;
        var todayStr = today.ToString("yyyy-MM-dd");
        var stats = new StatsCache
        {
            DailyActivity =
            [
                new DailyActivityEntry { Date = todayStr, MessageCount = 42, SessionCount = 3 }
            ],
            DailyModelTokens =
            [
                new DailyModelTokensEntry
                {
                    Date = todayStr,
                    TokensByModel = new() { ["claude-sonnet"] = 15_000 }
                }
            ]
        };

        var calc = CreateCalculator();
        var result = calc.Calculate(stats, null);

        Assert.Equal(42, result.TodayMessages);
        Assert.Equal(3, result.TodaySessions);
        Assert.Equal(15_000, result.TodayTokens);
    }

    [Fact]
    public void DailyActivity_Always7Entries_FillsMissingWithZeros()
    {
        var stats = new StatsCache();
        var calc = CreateCalculator();
        var result = calc.Calculate(stats, null);

        Assert.Equal(7, result.DailyActivity.Count);
        Assert.All(result.DailyActivity, day =>
        {
            Assert.Equal(0, day.Messages);
            Assert.Equal(0, day.Tokens);
        });
    }

    [Theory]
    [InlineData(30_000, "30s")]
    [InlineData(59_999, "60s")]
    [InlineData(60_000, "1m")]
    [InlineData(300_000, "5m")]
    [InlineData(3_599_999, "60m")]
    [InlineData(3_600_000, "1.0h")]
    [InlineData(7_200_000, "2.0h")]
    public void TimeSavedFormatted_CorrectUnits(long savedMs, string expected)
    {
        var stats = new StatsCache { TotalSpeculationTimeSavedMs = savedMs };
        var calc = CreateCalculator();
        var result = calc.Calculate(stats, null);

        Assert.Equal(expected, result.TimeSavedFormatted);
    }

    [Fact]
    public void EstimatedCostUsd_SumsModelUsageCosts()
    {
        var stats = new StatsCache
        {
            ModelUsage = new()
            {
                ["claude-sonnet"] = new ModelUsageEntry { CostUSD = 1.50 },
                ["claude-opus"] = new ModelUsageEntry { CostUSD = 3.25 }
            }
        };

        var calc = CreateCalculator();
        var result = calc.Calculate(stats, null);

        Assert.Equal(4.75, result.EstimatedCostUsd, precision: 2);
    }

    [Fact]
    public void LifetimeTotals_FromStatsCache()
    {
        var stats = new StatsCache
        {
            TotalSessions = 150,
            TotalMessages = 3000
        };

        var calc = CreateCalculator();
        var result = calc.Calculate(stats, null);

        Assert.Equal(150, result.TotalSessions);
        Assert.Equal(3000, result.TotalMessages);
    }
}
