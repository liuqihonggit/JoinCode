namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldThresholdsTests
{
    [Fact]
    public void Default_PruneProtectTokens_Is40k_AlignedWithOpenCode()
    {
        ContextFoldThresholds.Default.PruneProtectTokens.Should().Be(40_000);
    }

    [Fact]
    public void Default_PruneMinimumTokens_Is20k_AlignedWithOpenCode()
    {
        ContextFoldThresholds.Default.PruneMinimumTokens.Should().Be(20_000);
    }

    [Fact]
    public void Custom_PruneThresholds_CanBeOverridden()
    {
        var t = new ContextFoldThresholds { PruneProtectTokens = 60_000, PruneMinimumTokens = 30_000 };

        t.PruneProtectTokens.Should().Be(60_000);
        t.PruneMinimumTokens.Should().Be(30_000);
    }

    [Fact]
    public void Default_PreservesExistingThresholds()
    {
        var t = ContextFoldThresholds.Default;
        t.FoldThreshold.Should().Be(0.5);
        t.AggressiveThreshold.Should().Be(0.7);
        t.ForceSummaryThreshold.Should().Be(0.8);
        t.EmergencyThreshold.Should().Be(0.95);
        t.RecentKeepTailMessages.Should().Be(2);
        t.CharsPerToken.Should().Be(4);
    }
}
