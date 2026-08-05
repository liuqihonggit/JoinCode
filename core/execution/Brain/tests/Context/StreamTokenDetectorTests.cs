namespace Core.Context;

public class StreamTokenDetectorTests
{
    [Fact]
    public void Ingest_WritesToRingBuffer_TokenCountIncrements()
    {
        using var detector = new StreamTokenDetector(windowCapacity: 100, detectInterval: TimeSpan.FromMilliseconds(50));
        detector.TokenCount.Should().Be(0);

        detector.Ingest("hello");
        detector.Ingest("world");

        detector.TokenCount.Should().Be(2);
    }

    [Fact]
    public void DetectNow_EmptyWindow_ReturnsNoLoop()
    {
        using var detector = new StreamTokenDetector();
        var result = detector.DetectNow();
        result.IsLoopDetected.Should().BeFalse();
    }

    [Fact]
    public void DetectNow_NonRepeatingTokens_ReturnsNoLoop()
    {
        using var detector = new StreamTokenDetector(windowCapacity: 100);
        foreach (var t in new[] { "apple", "banana", "cherry", "date", "elderberry" })
            detector.Ingest(t);

        var result = detector.DetectNow();
        result.IsLoopDetected.Should().BeFalse();
    }

    [Fact]
    public void DetectNow_RepeatingPattern_ReturnsLoop()
    {
        using var detector = new StreamTokenDetector(
            windowCapacity: 50, minPatternLength: 2, requiredRepeats: 3);
        foreach (var t in new[] { "read", "grep", "read", "grep", "read", "grep" })
            detector.Ingest(t);

        var result = detector.DetectNow();
        result.IsLoopDetected.Should().BeTrue();
        result.RepeatCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void DetectNow_SingleTokenRepeating_ReturnsLoop()
    {
        using var detector = new StreamTokenDetector(
            windowCapacity: 50, minPatternLength: 1, requiredRepeats: 4);
        foreach (var t in new[] { "yes", "yes", "yes", "yes", "yes" })
            detector.Ingest(t);

        var result = detector.DetectNow();
        result.IsLoopDetected.Should().BeTrue();
    }

    [Fact]
    public void Reset_ClearsWindow()
    {
        using var detector = new StreamTokenDetector();
        detector.Ingest("a");
        detector.Ingest("b");
        detector.TokenCount.Should().Be(2);

        detector.Reset();

        detector.TokenCount.Should().Be(0);
        detector.GetLatestResult().Should().BeNull();
    }

    [Fact]
    public void Dispose_StopsBackgroundThread_DoesNotHang()
    {
        var detector = new StreamTokenDetector(detectInterval: TimeSpan.FromMilliseconds(10));
        detector.Ingest("test");

        var act = () => detector.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task BackgroundThread_DetectsLoop_AsyncResultAvailable()
    {
        using var detector = new StreamTokenDetector(
            windowCapacity: 50,
            detectInterval: TimeSpan.FromMilliseconds(20),
            minPatternLength: 1,
            requiredRepeats: 3);

        for (var i = 0; i < 6; i++)
            detector.Ingest("loop");

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && detector.GetLatestResult() is null)
            await Task.Delay(10);

        detector.GetLatestResult().Should().NotBeNull();
        detector.GetLatestResult()!.IsLoopDetected.Should().BeTrue();
    }

    [Fact]
    public void Funnel_TailRepetitionTriggersBeforeNgram()
    {
        using var detector = new StreamTokenDetector(
            windowCapacity: 50, minPatternLength: 2, requiredRepeats: 3);
        foreach (var t in new[] { "x", "y", "read", "grep", "read", "grep", "read", "grep" })
            detector.Ingest(t);

        var result = detector.DetectNow();
        result.IsLoopDetected.Should().BeTrue();
        result.RepeatedPattern.Should().NotBeNull();
    }

    [Fact]
    public void Ingest_OverCapacity_OverwritesOldest()
    {
        using var detector = new StreamTokenDetector(windowCapacity: 3);
        detector.Ingest("a");
        detector.Ingest("b");
        detector.Ingest("c");
        detector.Ingest("d");

        detector.TokenCount.Should().Be(3);
    }

    [Fact]
    public async Task BackgroundThread_NoLoop_LatestResultStaysNull()
    {
        using var detector = new StreamTokenDetector(
            windowCapacity: 100, detectInterval: TimeSpan.FromMilliseconds(20));
        foreach (var t in new[] { "alpha", "beta", "gamma", "delta" })
            detector.Ingest(t);

        await Task.Delay(100);

        detector.GetLatestResult().Should().BeNull();
    }

    [Fact]
    public async Task TriggerCount_IncrementsOnDetection()
    {
        using var detector = new StreamTokenDetector(
            windowCapacity: 50,
            detectInterval: TimeSpan.FromMilliseconds(20),
            minPatternLength: 1,
            requiredRepeats: 3);
        for (var i = 0; i < 6; i++)
            detector.Ingest("go");

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && detector.TriggerCount < 1)
            await Task.Delay(10);

        detector.TriggerCount.Should().BeGreaterThanOrEqualTo(1);
    }
}
