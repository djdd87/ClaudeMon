using ClaudeMon.ViewModels;

namespace ClaudeMon.Tests.ViewModels;

public class FormatTierTests
{
    [Theory]
    [InlineData("default_claude_max_5x", "", "Max 5x")]
    [InlineData("default_claude_max_20x", "", "Max 20x")]
    [InlineData("pro", "", "Pro")]
    [InlineData("default_claude_ai", "", "Pro")]
    [InlineData("default_raven", "", "Standard")]
    [InlineData("default_claude_max_5x", "team", "Team Premium")]
    [InlineData("default_claude_max_20x", "team", "Team Premium 20x")]
    [InlineData("pro", "team", "Team Pro")]
    [InlineData("default_claude_ai", "team", "Team Pro")]
    [InlineData("default_raven", "team", "Team Standard")]
    [InlineData("default_claude_ai", "pro", "Pro")]
    public void KnownPlans_ReturnFriendlyNames(string tier, string sub, string expected)
    {
        Assert.Equal(expected, ProfileViewModel.FormatTier(tier, sub));
    }

    [Fact]
    public void UnknownTier_FallbackFormatting()
    {
        var result = ProfileViewModel.FormatTier("default_claude_ultra", "");
        Assert.Equal("Ultra", result);
    }

    [Fact]
    public void UnknownTierWithSubscription_IncludesSubscription()
    {
        var result = ProfileViewModel.FormatTier("default_claude_ultra", "enterprise");
        Assert.Equal("Enterprise Ultra", result);
    }

    [Fact]
    public void BothNull_ReturnsUnknownPlan()
    {
        Assert.Equal("Unknown Plan", ProfileViewModel.FormatTier(null, null));
    }

    [Fact]
    public void BothEmpty_ReturnsUnknownPlan()
    {
        Assert.Equal("Unknown Plan", ProfileViewModel.FormatTier("", ""));
    }
}
