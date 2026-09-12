namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldStuckGuardTests
{
    [Fact]
    public void BeforeLimit_NotStuck()
    {
        ContextFoldDecider.IsFoldStuck(0, limit: 2).Should().BeFalse();
        ContextFoldDecider.IsFoldStuck(1, limit: 2).Should().BeFalse();
    }

    [Fact]
    public void AtOrPastLimit_Stuck()
    {
        ContextFoldDecider.IsFoldStuck(2, limit: 2).Should().BeTrue();
        ContextFoldDecider.IsFoldStuck(3, limit: 2).Should().BeTrue();
    }

    [Fact]
    public void DefaultLimit_MatchesThresholdDefault()
    {
        var t = ContextFoldThresholds.Default;
        ContextFoldDecider.IsFoldStuck(t.StuckFoldLimit, t.StuckFoldLimit).Should().BeTrue();
        ContextFoldDecider.IsFoldStuck(t.StuckFoldLimit - 1, t.StuckFoldLimit).Should().BeFalse();
    }
}