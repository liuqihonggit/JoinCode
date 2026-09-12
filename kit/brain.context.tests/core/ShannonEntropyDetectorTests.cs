namespace Core.Context;

public sealed class ShannonEntropyDetectorTests
{
    private static ShannonEntropyDetector CreateDetector(
        int windowSize = 10,
        int declineThreshold = 3,
        double minEntropyDelta = 0.001,
        TimeSpan? confirmationWindow = null,
        Func<DateTimeOffset>? clock = null)
        => new(windowSize, declineThreshold, minEntropyDelta,
              confirmationWindow ?? TimeSpan.FromSeconds(5), clock);

    private static readonly string HighEntropy =
        string.Concat(Enumerable.Range(0, 26).SelectMany(i => new string((char)('a' + i), 4)));

    private static readonly string MediumEntropy =
        string.Concat(Enumerable.Range(0, 5).SelectMany(i => new string((char)('a' + i), 8)));

    private static readonly string LowEntropy = new string('a', 30) + new string('b', 10);

    private static readonly string VeryLowEntropy = new string('a', 90) + new string('b', 10);

    private static readonly string EvenLowerEntropy = new string('a', 500) + new string('b', 10);

    private static readonly string EvenLower2Entropy = new string('a', 2000) + new string('b', 10);

    [Fact]
    public void Record_ShortText_ReturnsNoLoop()
    {
        var sut = CreateDetector();
        var result = sut.Record("短文本");

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_NormalText_ReturnsNoLoop()
    {
        var sut = CreateDetector(declineThreshold: 4, minEntropyDelta: 0.05);
        var text = "这是一段正常的文本内容，包含了各种不同的字符和词汇。";

        var result = sut.Record(text);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_FirstDecline_EntersSuspected_NotConfirmed()
    {
        var sut = CreateDetector(declineThreshold: 3, minEntropyDelta: 0.001);

        sut.Record(HighEntropy);
        sut.Record(MediumEntropy);
        sut.Record(LowEntropy);
        var result = sut.Record(VeryLowEntropy);

        Assert.Equal(EntropyDetectionState.Suspected, result.State);
        Assert.False(result.IsLoopDetected);
        Assert.True(result.DeclineStreak >= 3);
    }

    [Fact]
    public void Record_SecondDeclineWithinWindow_Confirmed()
    {
        var time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var sut = CreateDetector(
            declineThreshold: 3, minEntropyDelta: 0.001,
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => time);

        sut.Record(HighEntropy);
        sut.Record(MediumEntropy);
        sut.Record(LowEntropy);
        sut.Record(VeryLowEntropy);

        time = time.AddSeconds(3);
        var result = sut.Record(EvenLowerEntropy);

        Assert.Equal(EntropyDetectionState.Confirmed, result.State);
        Assert.True(result.IsLoopDetected);
        Assert.Equal(1, result.TriggerCount);
    }

    [Fact]
    public void Record_SecondDeclineAfterTimeout_ResetsToSuspected()
    {
        var time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var sut = CreateDetector(
            declineThreshold: 3, minEntropyDelta: 0.001,
            confirmationWindow: TimeSpan.FromSeconds(5),
            clock: () => time);

        sut.Record(HighEntropy);
        sut.Record(MediumEntropy);
        sut.Record(LowEntropy);
        sut.Record(VeryLowEntropy);

        time = time.AddSeconds(6);
        var result = sut.Record(EvenLowerEntropy);

        Assert.Equal(EntropyDetectionState.Suspected, result.State);
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_ConfirmedThenRecover_BackToMonitoring()
    {
        var time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var sut = CreateDetector(
            declineThreshold: 3, minEntropyDelta: 0.001,
            clock: () => time);

        sut.Record(HighEntropy);
        sut.Record(MediumEntropy);
        sut.Record(LowEntropy);
        sut.Record(VeryLowEntropy);

        time = time.AddSeconds(1);
        var confirmed = sut.Record(EvenLowerEntropy);
        Assert.Equal(EntropyDetectionState.Confirmed, confirmed.State);

        var result = sut.Record(HighEntropy);

        Assert.Equal(EntropyDetectionState.Monitoring, result.State);
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_Confirmed_ContinuesReportingWithIncrement()
    {
        var time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var sut = CreateDetector(
            declineThreshold: 3, minEntropyDelta: 0.001,
            clock: () => time);

        sut.Record(HighEntropy);
        sut.Record(MediumEntropy);
        sut.Record(LowEntropy);
        sut.Record(VeryLowEntropy);

        time = time.AddSeconds(1);
        var r1 = sut.Record(EvenLowerEntropy);
        Assert.Equal(1, r1.TriggerCount);

        var r2 = sut.Record(EvenLower2Entropy);
        Assert.Equal(EntropyDetectionState.Confirmed, r2.State);
        Assert.True(r2.IsLoopDetected);
        Assert.Equal(2, r2.TriggerCount);
    }

    [Fact]
    public void Record_StableEntropy_NoLoop()
    {
        var sut = CreateDetector(declineThreshold: 3, minEntropyDelta: 0.05);

        var text = "这是一段正常的文本内容，包含了各种不同的字符。";
        for (var i = 0; i < 10; i++)
        {
            var result = sut.Record(text);
            Assert.False(result.IsLoopDetected);
        }
    }

    [Fact]
    public void Record_EntropyIncrease_NoLoop()
    {
        var sut = CreateDetector(declineThreshold: 3, minEntropyDelta: 0.01);

        var low = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var medium = "aaaaabbbbbcccccdddddeeeeefffffggggghhhhh";
        var high = "abcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()";

        sut.Record(low);
        sut.Record(medium);
        var result = sut.Record(high);

        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var sut = CreateDetector(declineThreshold: 3, minEntropyDelta: 0.001, clock: () => time);

        sut.Record(HighEntropy);
        sut.Record(MediumEntropy);
        sut.Record(LowEntropy);
        sut.Record(VeryLowEntropy);

        time = time.AddSeconds(1);
        sut.Record(EvenLowerEntropy);
        Assert.Equal(EntropyDetectionState.Confirmed, sut.State);

        sut.Reset();

        Assert.Equal(EntropyDetectionState.Monitoring, sut.State);
        Assert.Equal(0, sut.TriggerCount);

        var result = sut.Record(VeryLowEntropy);
        Assert.False(result.IsLoopDetected);
    }

    [Fact]
    public void Record_CurrentEntropy_ReturnedCorrectly()
    {
        var sut = CreateDetector();
        var text = "这是一段正常的文本内容，包含了各种不同的字符。";

        var result = sut.Record(text);

        Assert.True(result.CurrentEntropy > 0);
    }

    [Fact]
    public void Record_PureRepeatingChars_LowEntropy()
    {
        var sut = CreateDetector();
        var text = new string('A', 100);

        var result = sut.Record(text);

        Assert.True(result.CurrentEntropy < 0.01);
    }

    [Fact]
    public void Record_UniformChars_HighEntropy()
    {
        var sut = CreateDetector();
        var text = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        var result = sut.Record(text);

        Assert.True(result.CurrentEntropy > 4.0);
    }

    [Fact]
    public void Record_NullText_Throws()
    {
        var sut = CreateDetector();
        Assert.Throws<ArgumentNullException>(() => sut.Record(null!));
    }

    [Fact]
    public void NoLoop_StaticProperty_HasDefaultValues()
    {
        Assert.Equal(EntropyDetectionState.Monitoring, ShannonEntropyResult.NoLoop.State);
        Assert.False(ShannonEntropyResult.NoLoop.IsLoopDetected);
        Assert.Equal(0, ShannonEntropyResult.NoLoop.CurrentEntropy);
        Assert.Equal(0, ShannonEntropyResult.NoLoop.DeclineStreak);
    }

    [Fact]
    public void Record_ShortText_PreservesState()
    {
        var time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var sut = CreateDetector(declineThreshold: 3, minEntropyDelta: 0.001, clock: () => time);

        sut.Record(HighEntropy);
        sut.Record(MediumEntropy);
        sut.Record(LowEntropy);
        sut.Record(VeryLowEntropy);
        Assert.Equal(EntropyDetectionState.Suspected, sut.State);

        var result = sut.Record("短");

        Assert.Equal(EntropyDetectionState.Suspected, result.State);
        Assert.False(result.IsLoopDetected);
    }
}
