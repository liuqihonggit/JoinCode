namespace Core.Context;

public class ReasoningRoundTests
{
    [Fact]
    public void StartEndRound_BasicRecordCreated()
    {
        var (recorder, clock) = CreateRecorder();

        recorder.StartRound();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        var round = recorder.EndRound(responseText: "hello");

        round.Turn.Should().Be(1);
        round.ResponseText.Should().Be("hello");
        round.Duration.Should().Be(TimeSpan.FromMilliseconds(500));
        recorder.Count.Should().Be(1);
    }

    [Fact]
    public void EndRound_AllFields_Preserved()
    {
        var (recorder, clock) = CreateRecorder();

        recorder.StartRound();
        clock.Advance(TimeSpan.FromSeconds(2));
        var round = recorder.EndRound(
            responseText: "response",
            thinkingText: "thinking",
            logicFingerprint: 12345,
            shannonEntropy: 3.14,
            toolCalls: new[] { "read", "grep" },
            isLoopDetected: true,
            loopReason: "尾重复");

        round.ResponseText.Should().Be("response");
        round.ThinkingText.Should().Be("thinking");
        round.LogicFingerprint.Should().Be(12345);
        round.ShannonEntropy.Should().Be(3.14);
        round.ToolCalls.Should().Equal("read", "grep");
        round.IsLoopDetected.Should().BeTrue();
        round.LoopReason.Should().Be("尾重复");
        round.Duration.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MultipleRounds_TurnIncrements()
    {
        var (recorder, clock) = CreateRecorder();

        recorder.StartRound();
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var r1 = recorder.EndRound();

        recorder.StartRound();
        clock.Advance(TimeSpan.FromMilliseconds(200));
        var r2 = recorder.EndRound();

        r1.Turn.Should().Be(1);
        r2.Turn.Should().Be(2);
        recorder.Count.Should().Be(2);
        recorder.CurrentTurn.Should().Be(2);
    }

    [Fact]
    public void GetRounds_ReturnsSnapshot_OldestToLatest()
    {
        var (recorder, clock) = CreateRecorder();

        for (var i = 0; i < 3; i++)
        {
            recorder.StartRound();
            clock.Advance(TimeSpan.FromMilliseconds(50));
            recorder.EndRound(responseText: $"round{i}");
        }

        var rounds = recorder.GetRounds();
        rounds.Should().HaveCount(3);
        rounds[0].ResponseText.Should().Be("round0");
        rounds[2].ResponseText.Should().Be("round2");
    }

    [Fact]
    public void OverCapacity_OverwritesOldest()
    {
        var (recorder, clock) = CreateRecorder(capacity: 3);

        for (var i = 0; i < 5; i++)
        {
            recorder.StartRound();
            clock.Advance(TimeSpan.FromMilliseconds(10));
            recorder.EndRound(responseText: $"r{i}");
        }

        var rounds = recorder.GetRounds();
        rounds.Should().HaveCount(3);
        rounds[0].ResponseText.Should().Be("r2");
        rounds[2].ResponseText.Should().Be("r4");
    }

    [Fact]
    public void Reset_ClearsAllRounds()
    {
        var (recorder, clock) = CreateRecorder();

        recorder.StartRound();
        recorder.EndRound();
        recorder.Count.Should().Be(1);

        recorder.Reset();

        recorder.Count.Should().Be(0);
        recorder.CurrentTurn.Should().Be(0);
    }

    [Fact]
    public void Duration_CalculatedFromStartEnd()
    {
        var (recorder, clock) = CreateRecorder();

        recorder.StartRound();
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(2));
        var round = recorder.EndRound();

        round.Duration.TotalSeconds.Should().Be(3);
    }

    private static (ReasoningRoundRecorder recorder, FakeTimeProvider clock) CreateRecorder(int capacity = 50)
    {
        var clock = new FakeTimeProvider();
        var recorder = new ReasoningRoundRecorder(capacity, clock);
        return (recorder, clock);
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void HundredThousandRounds_MemoryDoesNotExplode()
    {
        var (recorder, clock) = CreateRecorder(capacity: 50);

var random = new Random(42);
        var texts = new string[100];
        for (var i = 0; i < 100; i++)
        {
            var chars = new char[5000];
            for (var j = 0; j < 5000; j++)
                chars[j] = (char)('a' + random.Next(26));
            texts[i] = new string(chars);
        }

        GC.Collect();
        GC.WaitForFullGCComplete();
        var memBefore = GC.GetTotalMemory(false);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100_000; i++)
        {
            recorder.StartRound();
            clock.Advance(TimeSpan.FromMilliseconds(1));
            recorder.EndRound(
                responseText: texts[i % 100],
                thinkingText: texts[(i + 50) % 100],
                logicFingerprint: i,
                shannonEntropy: 3.14,
                toolCalls: new[] { "read", "grep", "edit" },
                isLoopDetected: i % 100 == 0,
                loopReason: "test");
        }
        sw.Stop();

        GC.Collect();
        GC.WaitForFullGCComplete();
        var memAfter = GC.GetTotalMemory(false);
        var memGrowthMB = (memAfter - memBefore) / 1024.0 / 1024.0;

        recorder.Count.Should().Be(50, "capacity=50,超出覆盖最旧");
        var rounds = recorder.GetRounds();
        rounds[^1].Turn.Should().Be(100_000, "最新轮次应为10万");
sw.ElapsedMilliseconds.Should().BeLessThan(3000, $"10万轮(5000字符随机上下文)应在3秒内: {sw.ElapsedMilliseconds}ms");
        memGrowthMB.Should().BeLessThan(50, $"内存增长应小于50MB(RingBuffer定长覆盖): {memGrowthMB:F1}MB");
    }
}

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public void Advance(TimeSpan duration) => _now += duration;
    public override DateTimeOffset GetUtcNow() => _now;
}
