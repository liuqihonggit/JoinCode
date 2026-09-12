namespace Core.Context.Compact;

public sealed class AutoCompactSoftThresholdTests
{
    [Fact]
    public void ShouldAutoCompact_BelowSoftThreshold_ReturnsFalse()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var currentTokens = 80_000;

        sut.ShouldAutoCompact(currentTokens, contextWindow).Should().BeFalse();
    }

    [Fact]
    public void ShouldAutoCompact_AtSoftThreshold_ReturnsFalse()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var currentTokens = 100_000;

        sut.ShouldAutoCompact(currentTokens, contextWindow).Should().BeFalse(
            "soft threshold (50%) should not trigger auto-compact");
    }

    [Fact]
    public void ShouldAutoCompact_BetweenSoftAndHard_ReturnsFalse()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var currentTokens = 140_000;

        sut.ShouldAutoCompact(currentTokens, contextWindow).Should().BeFalse(
            "between soft (50%) and hard threshold should not trigger auto-compact to protect prefix cache");
    }

    [Fact]
    public void ShouldAutoCompact_AtHardThreshold_ReturnsTrue()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var hardThreshold = contextWindow - 20_000 - 13_000;

        sut.ShouldAutoCompact(hardThreshold, contextWindow).Should().BeTrue(
            "hard threshold should trigger auto-compact");
    }

    [Fact]
    public void ShouldSoftCompactNotice_BelowSoftThreshold_ReturnsFalse()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var currentTokens = 80_000;

        sut.ShouldSoftCompactNotice(currentTokens, contextWindow).Should().BeFalse();
    }

    [Fact]
    public void ShouldSoftCompactNotice_AtSoftThreshold_ReturnsTrue()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var currentTokens = 100_000;

        sut.ShouldSoftCompactNotice(currentTokens, contextWindow).Should().BeTrue(
            "soft threshold (50%) should trigger notice");
    }

    [Fact]
    public void ShouldSoftCompactNotice_BetweenSoftAndHard_ReturnsTrue()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var currentTokens = 140_000;

        sut.ShouldSoftCompactNotice(currentTokens, contextWindow).Should().BeTrue(
            "between soft and hard should still trigger notice");
    }

    [Fact]
    public void ShouldSoftCompactNotice_AtHardThreshold_ReturnsFalse()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var hardThreshold = contextWindow - 20_000 - 13_000;

        sut.ShouldSoftCompactNotice(hardThreshold, contextWindow).Should().BeFalse(
            "at hard threshold, notice is no longer needed — compact will trigger instead");
    }

    [Fact]
    public void CalculateWarningState_SoftThreshold_SetsFlag()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var currentTokens = 100_000;

        var state = sut.CalculateWarningState(currentTokens, contextWindow);

        state.IsAboveSoftCompactThreshold.Should().BeTrue();
        state.IsAboveAutoCompactThreshold.Should().BeFalse();
    }

    [Fact]
    public void CalculateWarningState_BelowSoftThreshold_NoFlag()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var currentTokens = 80_000;

        var state = sut.CalculateWarningState(currentTokens, contextWindow);

        state.IsAboveSoftCompactThreshold.Should().BeFalse();
    }

    [Fact]
    public void ShouldSoftCompactNotice_OnlyNoticesOnce()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;
        var currentTokens = 100_000;

        sut.ShouldSoftCompactNotice(currentTokens, contextWindow).Should().BeTrue("first notice");
        sut.ShouldSoftCompactNotice(currentTokens, contextWindow).Should().BeFalse("already noticed");
    }

    [Fact]
    public void ShouldSoftCompactNotice_ResetsWhenBelowSoftThreshold()
    {
        var sut = CreateSut();
        var contextWindow = 200_000;

        sut.ShouldSoftCompactNotice(100_000, contextWindow).Should().BeTrue("first notice");
        sut.ShouldSoftCompactNotice(80_000, contextWindow).Should().BeFalse("below threshold");
        sut.ShouldSoftCompactNotice(100_000, contextWindow).Should().BeTrue("notice again after reset");
    }

    private static AutoCompactService CreateSut(double? softCompactRatio = null)
    {
        var thresholds = new CompactThresholds();
        if (softCompactRatio.HasValue)
        {
            thresholds = new CompactThresholds { SoftCompactRatio = softCompactRatio.Value };
        }

        return new AutoCompactService(
            new MiddlewarePipeline<CompactContext>([]),
            new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance),
            Options.Create(thresholds));
    }
}
