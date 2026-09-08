namespace Core.Utils;

/// <summary>
/// ActorBase 单元测试 — 验证命令串行处理、异常容错、生命周期、背压、输出流。
/// </summary>
public class ActorBaseTest
{
    [Fact]
    public async Task SendAsync_CommandProcessed_OutputReceived()
    {
        await using var actor = new TestActor();
        await actor.SendAsync("hello");
        await actor.SendAsync("world");

        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 2, TimeSpan.FromSeconds(5));

        actor.ProcessedCommands.Should().Equal("hello", "world");
    }

    [Fact]
    public async Task TrySend_CommandProcessed_ReturnsTrue()
    {
        await using var actor = new TestActor();
        actor.TrySend("test").Should().BeTrue();
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromSeconds(5));
        actor.ProcessedCommands.Should().Contain("test");
    }

    [Fact]
    public async Task MultipleCommands_ProcessedSerially_InOrder()
    {
        await using var actor = new TestActor();
        for (var i = 0; i < 100; i++)
            await actor.SendAsync($"msg-{i}");

        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 100, TimeSpan.FromSeconds(10));

        actor.ProcessedCommands.Should().HaveCount(100);
        for (var i = 0; i < 100; i++)
            actor.ProcessedCommands[i].Should().Be($"msg-{i}");
    }

    [Fact]
    public async Task ConcurrentSend_AllCommandsProcessed_NoLoss()
    {
        await using var actor = new TestActor();
        var tasks = Enumerable.Range(0, 500)
            .Select(i => actor.SendAsync($"msg-{i}").AsTask())
            .ToArray();

        await Task.WhenAll(tasks);
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 500, TimeSpan.FromSeconds(10));

        actor.ProcessedCommands.Should().HaveCount(500);
    }

    [Fact]
    public async Task CommandThrows_ConsumerContinues_NextCommandSucceeds()
    {
        await using var actor = new TestActor();
        await actor.SendAsync("throw");
        await actor.SendAsync("normal");

        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromSeconds(5));

        actor.ProcessedCommands.Should().Contain("normal");
        actor.ErrorCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_TrySendReturnsFalse()
    {
        var actor = new TestActor();
        await actor.DisposeAsync();

        actor.TrySend("test").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var actor = new TestActor();
        await actor.DisposeAsync();

        var act = async () => await actor.SendAsync("test");
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task OutputAsync_ReceivesPublishedMessages()
    {
        await using var actor = new TestActor();
        await actor.SendAsync("hello");

        await WaitUntilAsync(() => actor.OutputCount >= 1, TimeSpan.FromSeconds(5));

        var output = await actor.OutputAsync().FirstOrDefaultAsync();
        output.Should().Be("processed-hello");
    }

    [Fact]
    public async Task BoundedChannel_ProcessesAllCommandsNoLoss()
    {
        await using var actor = new TestActor(boundedCapacity: 4);
        var tasks = Enumerable.Range(0, 100)
            .Select(i => actor.SendAsync($"msg-{i}").AsTask())
            .ToArray();

        await Task.WhenAll(tasks);
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 100, TimeSpan.FromSeconds(10));

        actor.ProcessedCommands.Should().HaveCount(100);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForConsumerExit()
    {
        var actor = new TestActor();
        await actor.SendAsync("test");
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromSeconds(5));

        await actor.DisposeAsync();
        actor.ConsumerTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WithBackpressure_WatermarkEventTriggered()
    {
        var bp = new ActorBackpressure(Capacity: 2, SendTimeout: TimeSpan.FromSeconds(1));
        await using var actor = new TestActor(bp);

        var events = new List<BackpressureEventArgs>();
        actor.InputWatermarkReached += (_, e) => events.Add(e);

        await actor.SendAsync("a");
        await actor.SendAsync("b");

        events.Should().Contain(e => e.Level == WatermarkLevel.High || e.Level == WatermarkLevel.Critical);
    }

    [Fact]
    public async Task OutputCount_ReflectsPublishedMessages()
    {
        await using var actor = new TestActor();
        actor.OutputCount.Should().Be(0);

        await actor.SendAsync("test");
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromSeconds(5));

        actor.OutputCount.Should().Be(1);
    }

    [Fact]
    public async Task OutputAsync_CancellationToken_CancelsStream()
    {
        await using var actor = new TestActor();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await actor.OutputAsync(cts.Token).ToListAsync();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task OutputAsync_SingleConsumer_ReceivesAllMessages()
    {
        await using var actor = new TestActor();
        await actor.SendAsync("test");
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromSeconds(5));

        var consumer = actor.OutputAsync().GetAsyncEnumerator();
        (await consumer.MoveNextAsync()).Should().BeTrue();
        consumer.Current.Should().Be("processed-test");
    }

    [Fact]
    public async Task SendAsync_Timeout_ThrowsTimeoutException()
    {
        var bp = new ActorBackpressure(Capacity: 1, SendTimeout: TimeSpan.FromMilliseconds(100));
        var gate = new TaskCompletionSource();
        await using var actor = new TestActor(bp) { Gate = gate };

        await actor.SendAsync("first");
        await Task.Delay(50);

        await actor.SendAsync("second");

        var act = async () => await actor.SendAsync("third");
        await act.Should().ThrowAsync<TimeoutException>();

        gate.SetResult();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Condition not met within {timeout}");
    }
}

/// <summary>
/// 测试用 Actor — 输入 string，输出 "processed-{input}"。
/// </summary>
internal sealed class TestActor : ActorBase<string, string>
{
    public readonly List<string> ProcessedCommands = new();
    public int ErrorCount { get; private set; }
    public TaskCompletionSource? Gate;

    public TestActor(int? boundedCapacity = null)
        : base(boundedCapacity is null ? null : new ActorBackpressure(boundedCapacity.Value))
    {
    }

    public TestActor(ActorBackpressure? backpressure)
        : base(backpressure)
    {
    }

    protected override async ValueTask HandleAsync(string command, CancellationToken ct)
    {
        await Task.Yield();
        if (command == "throw")
            throw new InvalidOperationException("test error");
        if (Gate is not null) await Gate.Task;
        ProcessedCommands.Add(command);
        TryPublish($"processed-{command}");
    }

    protected override void OnConsumerError(Exception ex)
    {
        ErrorCount++;
    }
}
