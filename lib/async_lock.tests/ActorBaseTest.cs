namespace Core.Utils;

/// <summary>
/// ActorBase 单元测试 — 验证命令串行处理、异常容错、生命周期、背压、输出流。
/// </summary>
public class ActorBaseTest {
    /// <summary>验证命令发送后被正确处理并产生输出</summary>
    [Fact]
    public async Task SendAsync_CommandProcessed_OutputReceived() {
        await using var actor = new TestActor();
        await actor.SendAsync("hello");
        await actor.SendAsync("world");

        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 2, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().Equal("hello", "world");
    }

    /// <summary>验证尝试发送命令后返回 true</summary>
    [Fact]
    public async Task TrySend_CommandProcessed_ReturnsTrue() {
        await using var actor = new TestActor();
        actor.TrySend("test").Should().BeTrue();
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));
        actor.ProcessedCommands.Should().Contain("test");
    }

    /// <summary>验证多个命令按顺序串行处理</summary>
    [Fact]
    public async Task MultipleCommands_ProcessedSerially_InOrder() {
        await using var actor = new TestActor();
        for (var i = 0; i < 100; i++)
            await actor.SendAsync($"msg-{i}");

        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 100, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().HaveCount(100);
        for (var i = 0; i < 100; i++)
            actor.ProcessedCommands[i].Should().Be($"msg-{i}");
    }

    /// <summary>验证并发发送时所有命令都被处理无丢失</summary>
    [Fact]
    public async Task ConcurrentSend_AllCommandsProcessed_NoLoss() {
        await using var actor = new TestActor();
        var tasks = Enumerable.Range(0, 500)
            .Select(i => actor.SendAsync($"msg-{i}").AsTask())
            .ToArray();

        await Task.WhenAll(tasks);
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 500, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().HaveCount(500);
    }

    /// <summary>验证命令抛出异常后消费者继续处理下一条命令</summary>
    [Fact]
    public async Task CommandThrows_ConsumerContinues_NextCommandSucceeds() {
        await using var actor = new TestActor();
        await actor.SendAsync("throw");
        await actor.SendAsync("normal");

        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().Contain("normal");
        actor.ErrorCount.Should().Be(1);
    }

    /// <summary>验证异步释放后尝试发送返回 false</summary>
    [Fact]
    public async Task DisposeAsync_TrySendReturnsFalse() {
        var actor = new TestActor();
        await actor.DisposeAsync();

        actor.TrySend("test").Should().BeFalse();
    }

    /// <summary>验证释放后发送命令抛出 ObjectDisposedException</summary>
    [Fact]
    public async Task SendAsync_AfterDispose_ThrowsObjectDisposed() {
        var actor = new TestActor();
        await actor.DisposeAsync();

        var act = async () => await actor.SendAsync("test");
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>验证输出流接收已发布的消息</summary>
    [Fact]
    public async Task OutputAsync_ReceivesPublishedMessages() {
        await using var actor = new TestActor();
        await actor.SendAsync("hello");

        await WaitUntilAsync(() => actor.OutputCount >= 1, TimeSpan.FromMilliseconds(500));

        var output = await actor.OutputAsync().FirstOrDefaultAsync();
        output.Should().Be("processed-hello");
    }

    /// <summary>验证有界通道处理所有命令无丢失</summary>
    [Fact]
    public async Task BoundedChannel_ProcessesAllCommandsNoLoss() {
        await using var actor = new TestActor(boundedCapacity: 4);
        var tasks = Enumerable.Range(0, 100)
            .Select(i => actor.SendAsync($"msg-{i}").AsTask())
            .ToArray();

        await Task.WhenAll(tasks);
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 100, TimeSpan.FromMilliseconds(500));

        actor.ProcessedCommands.Should().HaveCount(100);
    }

    /// <summary>验证异步释放等待消费者退出</summary>
    [Fact]
    public async Task DisposeAsync_WaitsForConsumerExit() {
        var actor = new TestActor();
        await actor.SendAsync("test");
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));

        await actor.DisposeAsync();
        await actor.ConsumerTask.WaitAsync(TimeSpan.FromSeconds(5));
        actor.ConsumerTask.IsCompleted.Should().BeTrue();
    }

    /// <summary>验证背压模式下触发水位事件</summary>
    [Fact]
    public async Task SendAsync_WithBackpressure_WatermarkEventTriggered() {
        var bp = new ActorBackpressure(Capacity: 2, SendTimeout: TimeSpan.FromSeconds(1));
        await using var actor = new TestActor(bp);

        var events = new List<BackpressureEventArgs>();
        actor.InputWatermarkReached += (_, e) => events.Add(e);

        await actor.SendAsync("a");
        await actor.SendAsync("b");

        events.Should().Contain(e => e.Level == WatermarkLevel.High || e.Level == WatermarkLevel.Critical);
    }

    /// <summary>验证输出计数反映已发布的消息数</summary>
    [Fact]
    public async Task OutputCount_ReflectsPublishedMessages() {
        await using var actor = new TestActor();
        actor.OutputCount.Should().Be(0);

        await actor.SendAsync("test");
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));

        actor.OutputCount.Should().Be(1);
    }

    /// <summary>验证取消令牌能取消输出流</summary>
    [Fact]
    public async Task OutputAsync_CancellationToken_CancelsStream() {
        await using var actor = new TestActor();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await actor.OutputAsync(cts.Token).ToListAsync();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>验证单消费者接收所有输出消息</summary>
    [Fact]
    public async Task OutputAsync_SingleConsumer_ReceivesAllMessages() {
        await using var actor = new TestActor();
        await actor.SendAsync("test");
        await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));

        var consumer = actor.OutputAsync().GetAsyncEnumerator();
        (await consumer.MoveNextAsync()).Should().BeTrue();
        consumer.Current.Should().Be("processed-test");
    }

    /// <summary>验证发送超时抛出 TimeoutException</summary>
    [Fact]
    public async Task SendAsync_Timeout_ThrowsTimeoutException() {
        var bp = new ActorBackpressure(Capacity: 1, SendTimeout: TimeSpan.FromMilliseconds(100));
        await using var actor = new TestActor(bp) { Gate = new() };

        await actor.SendAsync("first");
        await WaitUntilAsync(() => actor.InputCount == 0, TimeSpan.FromMilliseconds(500));

        await actor.SendAsync("second");

        var act = async () => await actor.SendAsync("third");
        await act.Should().ThrowAsync<TimeoutException>();
    }

    /// <summary>验证通过 IActor 接口发送命令被正确处理</summary>
    [Fact]
    public async Task IActorInterface_SendAsync_CommandProcessed() {
        await using IActor<string> actor = new TestActor();
        await actor.SendAsync("via-interface");
        var concrete = (TestActor)actor;
        await WaitUntilAsync(() => concrete.ProcessedCommands.Count >= 1, TimeSpan.FromMilliseconds(500));
        concrete.ProcessedCommands.Should().Contain("via-interface");
    }

    /// <summary>验证 IActor 接口的 Id 和 InputCount 可访问</summary>
    [Fact]
    public async Task IActorInterface_TrySend_Id_InputCount_Accessible() {
        await using IActor<string> actor = new TestActor();
        actor.Id.Should().NotBeNullOrEmpty();
        actor.InputCount.Should().Be(0);
        actor.TrySend("test").Should().BeTrue();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan perRetryTimeout) {
        for (var i = 0; i < 16; i++) {
            var deadline = DateTimeOffset.UtcNow + perRetryTimeout;
            while (DateTimeOffset.UtcNow < deadline) {
                if (condition()) return;
                await Task.Delay(10);
            }
        }
        throw new TimeoutException($"等待条件超时,重试16次×{perRetryTimeout.TotalMilliseconds:F0}ms");
    }
}

/// <summary>
/// 测试用 Actor — 输入 string，输出 "processed-{input}"。
/// </summary>
internal sealed class TestActor : ActorBase<string, string> {
    public readonly List<string> ProcessedCommands = new();
    /// <summary>获取错误计数</summary>
    public int ErrorCount { get; private set; }
    public TaskCompletionSource? Gate;

    /// <summary>初始化测试 Actor</summary>
    /// <param name="boundedCapacity">有界容量（可选）</param>
    public TestActor(int? boundedCapacity = null)
        : base(boundedCapacity is null ? null : new ActorBackpressure(boundedCapacity.Value)) {
    }

    /// <summary>初始化测试 Actor</summary>
    /// <param name="backpressure">背压配置（可选）</param>
    public TestActor(ActorBackpressure? backpressure)
        : base(backpressure) {
    }

    protected override async ValueTask HandleAsync(string command, CancellationToken ct) {
        await Task.Yield();
        if (command == "throw")
            throw new InvalidOperationException("test error");
        if (Gate is not null) await Gate.Task.WaitAsync(ct);
        ProcessedCommands.Add(command);
        TryPublish($"processed-{command}");
    }

    protected override void OnConsumerError(Exception ex) {
        ErrorCount++;
    }
}