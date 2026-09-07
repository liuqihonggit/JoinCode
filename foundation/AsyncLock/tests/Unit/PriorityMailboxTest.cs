namespace Core.Utils;

/// <summary>
/// PriorityMailbox 单元测试 — 验证优先级消费顺序、FIFO、背压、Dispose、异常容错。
/// </summary>
public class PriorityMailboxTest
{
    [Fact]
    public async Task HighPriority_ProcessedBeforeLowPriority()
    {
        await using var actor = new PriorityTestActor();
        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        await actor.SendAsync(1, MessagePriority.Low);
        await actor.SendAsync(2, MessagePriority.High);
        await actor.SendAsync(3, MessagePriority.Low);

        gateTcs.SetResult();
        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 3, TimeSpan.FromSeconds(5));

        actor.ProcessedOrder.Should().Equal(2, 1, 3);
    }

    [Fact]
    public async Task NormalPriority_ProcessedBeforeLowPriority()
    {
        await using var actor = new PriorityTestActor();
        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        await actor.SendAsync(1, MessagePriority.Low);
        await actor.SendAsync(2, MessagePriority.Normal);
        await actor.SendAsync(3, MessagePriority.Low);

        gateTcs.SetResult();
        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 3, TimeSpan.FromSeconds(5));

        actor.ProcessedOrder.Should().Equal(2, 1, 3);
    }

    [Fact]
    public async Task HighPriority_ProcessedBeforeNormalPriority()
    {
        await using var actor = new PriorityTestActor();
        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        await actor.SendAsync(1, MessagePriority.Normal);
        await actor.SendAsync(2, MessagePriority.High);
        await actor.SendAsync(3, MessagePriority.Normal);

        gateTcs.SetResult();
        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 3, TimeSpan.FromSeconds(5));

        actor.ProcessedOrder.Should().Equal(2, 1, 3);
    }

    [Fact]
    public async Task SamePriority_FifoOrder()
    {
        await using var actor = new PriorityTestActor();
        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        for (var i = 1; i <= 5; i++)
        {
            await actor.SendAsync(i, MessagePriority.Normal);
        }

        gateTcs.SetResult();
        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 5, TimeSpan.FromSeconds(5));

        actor.ProcessedOrder.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task MixedPriority_HighAlwaysFirst()
    {
        await using var actor = new PriorityTestActor();
        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        await actor.SendAsync(10, MessagePriority.Low);
        await actor.SendAsync(20, MessagePriority.Normal);
        await actor.SendAsync(30, MessagePriority.High);
        await actor.SendAsync(11, MessagePriority.Low);
        await actor.SendAsync(21, MessagePriority.Normal);
        await actor.SendAsync(31, MessagePriority.High);

        gateTcs.SetResult();
        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 6, TimeSpan.FromSeconds(5));

        actor.ProcessedOrder.IndexOf(30).Should().BeLessThan(actor.ProcessedOrder.IndexOf(10));
        actor.ProcessedOrder.IndexOf(30).Should().BeLessThan(actor.ProcessedOrder.IndexOf(20));
        actor.ProcessedOrder.IndexOf(31).Should().BeLessThan(actor.ProcessedOrder.IndexOf(11));
        actor.ProcessedOrder.IndexOf(31).Should().BeLessThan(actor.ProcessedOrder.IndexOf(21));
    }

    [Fact]
    public async Task MailboxCount_ReflectsTotalAcrossChannels()
    {
        var bp = new ActorBackpressure(Capacity: 10);
        await using var actor = new PriorityTestActor(bp, bp, bp);

        await actor.SendAsync(1, MessagePriority.High);
        await actor.SendAsync(2, MessagePriority.Normal);
        await actor.SendAsync(3, MessagePriority.Low);

        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 3, TimeSpan.FromSeconds(5));
        actor.MailboxCount.Should().Be(0);
    }

    [Fact]
    public async Task Dispose_ThrowsObjectDisposedException_OnSend()
    {
        var actor = new PriorityTestActor();
        await actor.DisposeAsync();

        var act = async () => await actor.SendAsync(1, MessagePriority.High);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ConsumerError_DoesNotStopConsumer()
    {
        await using var actor = new ErrorTestActor();
        var tcs1 = new TaskCompletionSource<int>();
        var tcs2 = new TaskCompletionSource<int>();

        await actor.SendAsync(new ErrorTestActor.ThrowCommand(tcs1), MessagePriority.Normal);
        await tcs1.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await actor.SendAsync(new ErrorTestActor.NormalCommand(tcs2), MessagePriority.Normal);
        (await tcs2.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(1);
    }

    [Fact]
    public async Task TrySend_ReturnsTrue_WhenChannelHasSpace()
    {
        var bp = new ActorBackpressure(Capacity: 10);
        await using var actor = new PriorityTestActor(bp, bp, bp);

        actor.TrySend(42, MessagePriority.High).Should().BeTrue();

        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 1, TimeSpan.FromSeconds(5));
        actor.ProcessedOrder.Should().Contain(42);
    }

    [Fact]
    public async Task TrySend_ReturnsFalse_WhenBoundedChannelFull()
    {
        var bp = new ActorBackpressure(Capacity: 1, FullMode: BoundedChannelFullMode.Wait);
        await using var actor = new PriorityTestActor(bp, bp, bp);
        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        actor.TrySend(1, MessagePriority.High).Should().BeTrue();
        actor.TrySend(2, MessagePriority.High).Should().BeFalse();

        gateTcs.SetResult();
        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 1, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IndependentBackpressure_PerPriorityChannel()
    {
        var highBp = new ActorBackpressure(Capacity: 2, HighWatermark: 1, CriticalWatermark: 2);
        await using var actor = new PriorityTestActor(highBackpressure: highBp);
        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        await actor.SendAsync(1, MessagePriority.High);
        await actor.SendAsync(2, MessagePriority.High);

        actor.IsHighWatermark(MessagePriority.High).Should().BeTrue();
        actor.IsHighWatermark(MessagePriority.Normal).Should().BeFalse();
        actor.IsHighWatermark(MessagePriority.Low).Should().BeFalse();

        gateTcs.SetResult();
        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 2, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PriorityWatermarkReached_EventFires()
    {
        var highBp = new ActorBackpressure(Capacity: 10, HighWatermark: 2, CriticalWatermark: 8);
        await using var actor = new PriorityTestActor(highBackpressure: highBp);
        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        var events = new List<PriorityBackpressureEventArgs>();
        actor.PriorityWatermarkReached += (_, e) => events.Add(e);

        for (var i = 0; i < 4; i++)
        {
            await actor.SendAsync(i, MessagePriority.High);
        }

        events.Should().NotBeEmpty();
        events.Should().Contain(e => e.Priority == MessagePriority.High);
        events.Should().Contain(e => e.Level == WatermarkLevel.High);

        gateTcs.SetResult();
        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 4, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SendTimeout_ThrowsTimeoutException_WhenChannelFull()
    {
        var highBp = new ActorBackpressure(
            Capacity: 1,
            FullMode: BoundedChannelFullMode.Wait,
            SendTimeout: TimeSpan.FromMilliseconds(200));
        await using var actor = new PriorityTestActor(highBackpressure: highBp);
        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        await actor.SendAsync(1, MessagePriority.High);
        await Task.Delay(100);

        await actor.SendAsync(2, MessagePriority.High);

        var act = async () => await actor.SendAsync(3, MessagePriority.High);
        await act.Should().ThrowAsync<TimeoutException>();

        gateTcs.SetResult();
        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= 2, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConcurrentProducers_ThreadSafe()
    {
        await using var actor = new PriorityTestActor();
        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        const int producerCount = 4;
        const int messagesPerProducer = 25;

        var producers = Enumerable.Range(0, producerCount).Select(pid =>
            Task.Run(async () =>
            {
                for (var i = 0; i < messagesPerProducer; i++)
                {
                    var priority = (i % 3) switch
                    {
                        0 => MessagePriority.High,
                        1 => MessagePriority.Normal,
                        _ => MessagePriority.Low
                    };
                    await actor.SendAsync(pid * 100 + i, priority);
                }
            }));

        await Task.WhenAll(producers);
        gateTcs.SetResult();

        var expected = producerCount * messagesPerProducer;
        await WaitUntilAsync(() => actor.ProcessedOrder.Count >= expected, TimeSpan.FromSeconds(10));

        actor.ProcessedOrder.Should().HaveCount(expected);
        var expectedSet = Enumerable.Range(0, producerCount)
            .SelectMany(pid => Enumerable.Range(0, messagesPerProducer).Select(i => pid * 100 + i))
            .ToHashSet();
        actor.ProcessedOrder.ToHashSet().Should().BeEquivalentTo(expectedSet);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException($"等待条件超时({timeout.TotalSeconds:F0}s)");
            await Task.Delay(10);
        }
    }
}

/// <summary>
/// 优先级测试用 Actor — 命令为 int,处理顺序记录在 ProcessedOrder。
/// </summary>
internal sealed class PriorityTestActor : PriorityMailbox<int>
{
    public readonly List<int> ProcessedOrder = new();
    private TaskCompletionSource _gate = CreateCompletedGate();

    private static TaskCompletionSource CreateCompletedGate()
    {
        var tcs = new TaskCompletionSource();
        tcs.SetResult();
        return tcs;
    }

    public PriorityTestActor(
        ActorBackpressure? highBackpressure = null,
        ActorBackpressure? normalBackpressure = null,
        ActorBackpressure? lowBackpressure = null)
        : base(highBackpressure, normalBackpressure, lowBackpressure)
    {
    }

    public void SetGate(TaskCompletionSource gate) => _gate = gate;

    public ValueTask SendAsync(int command, MessagePriority priority) => base.SendAsync(command, priority);

    public new bool TrySend(int command, MessagePriority priority) => base.TrySend(command, priority);

    protected override async ValueTask HandleAsync(int command, MessagePriority priority, CancellationToken ct)
    {
        await _gate.Task.WaitAsync(ct);
        ProcessedOrder.Add(command);
    }
}

/// <summary>
/// 异常容错测试用 Actor — ThrowCommand 抛异常,NormalCommand 正常处理。
/// </summary>
internal sealed class ErrorTestActor : PriorityMailbox<ErrorTestActor.ICommand>
{
    internal interface ICommand;
    internal sealed record ThrowCommand(TaskCompletionSource<int> Tcs) : ICommand;
    internal sealed record NormalCommand(TaskCompletionSource<int> Tcs) : ICommand;

    private int _value;

    protected override async ValueTask HandleAsync(ICommand command, MessagePriority priority, CancellationToken ct)
    {
        switch (command)
        {
            case ThrowCommand(var tcs):
                tcs.TrySetResult(0);
                throw new InvalidOperationException("测试异常");
            case NormalCommand(var tcs):
                _value++;
                tcs.TrySetResult(_value);
                return;
        }
    }

    public ValueTask SendAsync(ICommand command, MessagePriority priority) => base.SendAsync(command, priority);
}
