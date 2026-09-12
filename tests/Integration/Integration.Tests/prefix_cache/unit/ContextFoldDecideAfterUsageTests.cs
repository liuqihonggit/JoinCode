namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldDecideAfterUsageTests
{
    private const int CtxMax = 1000;

    private static TokenUsage Usage(int prompt, int cacheRead = 0) =>
        new(prompt, 0) { CacheReadInputTokens = cacheRead };

    [Fact]
    public void HealthyCache_AtNormalRatio_Defers()
    {
        var decision = ContextFoldDecider.DecideAfterUsage(
            Usage(600, cacheRead: 500), CtxMax, alreadyFoldedThisTurn: false, deferralCount: 0);

        decision.Should().Be(ContextFoldDecision.Deferred);
    }

    [Fact]
    public void HealthyCache_AtAggressiveRatio_Defers()
    {
        var decision = ContextFoldDecider.DecideAfterUsage(
            Usage(750, cacheRead: 600), CtxMax, alreadyFoldedThisTurn: false, deferralCount: 0);

        decision.Should().Be(ContextFoldDecision.Deferred);
    }

    [Fact]
    public void HealthyCache_AtForceThreshold_AlwaysExits()
    {
        var decision = ContextFoldDecider.DecideAfterUsage(
            Usage(850, cacheRead: 700), CtxMax, alreadyFoldedThisTurn: false, deferralCount: 0);

        decision.Should().Be(ContextFoldDecision.ExitWithSummary);
    }

    [Fact]
    public void ColdCache_AtNormalRatio_FoldsNot()
    {
        var decision = ContextFoldDecider.DecideAfterUsage(
            Usage(600, cacheRead: 0), CtxMax, alreadyFoldedThisTurn: false, deferralCount: 0);

        decision.Should().Be(ContextFoldDecision.FoldNormal);
    }

    [Fact]
    public void HealthyCache_DeferralCapHit_ForcesFold()
    {
        var decision = ContextFoldDecider.DecideAfterUsage(
            Usage(600, cacheRead: 500), CtxMax, alreadyFoldedThisTurn: false, deferralCount: ContextFoldThresholds.Default.DeferFoldLimit);

        decision.Should().Be(ContextFoldDecision.FoldNormal);
    }

    [Fact]
    public void HealthyCache_BelowSoftThreshold_ReturnsNone()
    {
        var decision = ContextFoldDecider.DecideAfterUsage(
            Usage(400, cacheRead: 300), CtxMax, alreadyFoldedThisTurn: false, deferralCount: 0);

        decision.Should().Be(ContextFoldDecision.None);
    }

    [Fact]
    public void AlreadyFoldedThisTurn_HealthyCache_ReturnsNone()
    {
        var decision = ContextFoldDecider.DecideAfterUsage(
            Usage(700, cacheRead: 500), CtxMax, alreadyFoldedThisTurn: true, deferralCount: 0);

        decision.Should().Be(ContextFoldDecision.None);
    }
}