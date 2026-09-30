namespace Core.Utils;

/// <summary>
/// ActorBase 背压功能单元测试 — 验证水位线、发送超时、事件触发、向后兼容。
/// </summary>
public class ActorBackpressureTest {
    [Fact]
    public void ActorBackpressure_PredefinedConfigs_HaveCorrectCapacity() {
        ActorBackpressure.CodingAgentTask.Capacity.Should().Be(2000);
        ActorBackpressure.LlmGateway.Capacity.Should().Be(200);
        ActorBackpressure.Router.Capacity.Should().Be(1000);
        ActorBackpressure.Build.Capacity.Should().Be(100);
    }

    [Fact]
    public void ActorBackpressure_EffectiveWatermarks_DefaultToPercentage() {
        var bp = new ActorBackpressure(Capacity: 100);
        bp.EffectiveHighWatermark.Should().Be(80);
        bp.EffectiveCriticalWatermark.Should().Be(95);
    }

    [Fact]
    public void ActorBackpressure_EffectiveWatermarks_UseExplicitValues() {
        var bp = new ActorBackpressure(Capacity: 100, HighWatermark: 50, CriticalWatermark: 90);
        bp.EffectiveHighWatermark.Should().Be(50);
        bp.EffectiveCriticalWatermark.Should().Be(90);
    }

    [Fact]
    public async Task MailboxCount_ReflectsQueuedCommands() {
        await using var actor = new BackpressureTestActor(new ActorBackpressure(Capacity: 10));
        var tcs1 = new TaskCompletionSource<int>();
        var tcs2 = new TaskCompletionSource<int>();

        await actor.IncrementAsync(tcs1);
        await actor.IncrementAsync(tcs2);
        await tcs1.Task;
        await tcs2.Task;

        actor.InputCount.Should().Be(0);
    }

    [Fact]
    public async Task IsHighWatermark_TrueWhenCountExceedsThreshold() {
        var bp = new ActorBackpressure(Capacity: 10, HighWatermark: 3, CriticalWatermark: 8);
        await using var actor = new BackpressureTestActor(bp);

        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        for (var i = 0; i < 5; i++) {
            await actor.IncrementAsync(new TaskCompletionSource<int>());
        }

        actor.IsInputHighWatermark.Should().BeTrue();
        actor.IsInputCriticalWatermark.Should().BeFalse();

        gateTcs.SetResult();
    }

    [Fact]
    public async Task IsCriticalWatermark_TrueWhenCountExceedsCritical() {
        var bp = new ActorBackpressure(Capacity: 20, HighWatermark: 5, CriticalWatermark: 10);
        await using var actor = new BackpressureTestActor(bp);

        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        for (var i = 0; i < 12; i++) {
            await actor.IncrementAsync(new TaskCompletionSource<int>());
        }

        actor.IsInputCriticalWatermark.Should().BeTrue();

        gateTcs.SetResult();
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task WatermarkReached_EventFiresOnHighWatermark() {
        var bp = new ActorBackpressure(Capacity: 20, HighWatermark: 3, CriticalWatermark: 15);
        await using var actor = new BackpressureTestActor(bp);

        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        var events = new List<BackpressureEventArgs>();
        actor.InputWatermarkReached += (_, e) => events.Add(e);

        for (var i = 0; i < 5; i++) {
            await actor.IncrementAsync(new TaskCompletionSource<int>());
        }

        events.Should().NotBeEmpty();
        events.Should().Contain(e => e.Level == WatermarkLevel.High);

        gateTcs.SetResult();
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task WatermarkReached_EventFiresOnCriticalWatermark() {
        var bp = new ActorBackpressure(Capacity: 30, HighWatermark: 5, CriticalWatermark: 10);
        await using var actor = new TestActor(bp);

        var gate = new TaskCompletionSource();
        actor.Gate = gate;

        var events = new ConcurrentQueue<BackpressureEventArgs>();
        actor.InputWatermarkReached += (_, e) => events.Enqueue(e);

        for (var i = 0; i < 15; i++) {
            actor.Tell($"msg-{i}");
        }

        await TestWaitHelper.WaitUntilAsync(() => events.Any(e => e.Level == WatermarkLevel.Critical), TimeSpan.FromMilliseconds(2000));

        gate.SetResult();
    }

    [Fact]
    public async Task SendTimeout_StartsBackgroundRetry_WhenChannelFull() {
        var bp = new ActorBackpressure(
            Capacity: 1,
            FullMode: BoundedChannelFullMode.Wait,
            SendTimeout: TimeSpan.FromMilliseconds(200));
        await using var actor = new BackpressureTestActor(bp);

        var gateTcs = new TaskCompletionSource();
        actor.SetGate(gateTcs);

        await actor.IncrementAsync(new TaskCompletionSource<int>());
        await Task.Delay(100);

        await actor.IncrementAsync(new TaskCompletionSource<int>());

        var act = async () => await actor.IncrementAsync(new TaskCompletionSource<int>());
        await act.Should().NotThrowAsync();

        gateTcs.SetResult();
    }

    [Fact]
    public async Task BackpressureConstructor_BackwardCompatible_WithExistingConstructor() {
        await using var actor1 = new BackpressureTestActor(boundedCapacity: 5);
        await using var actor2 = new BackpressureTestActor();

        var tcs = new TaskCompletionSource<int>();
        await actor1.IncrementAsync(tcs);
        (await tcs.Task).Should().Be(1);

        var tcs2 = new TaskCompletionSource<int>();
        await actor2.IncrementAsync(tcs2);
        (await tcs2.Task).Should().Be(1);
    }

    [Fact]
    public async Task NoBackpressure_IsHighWatermark_AlwaysFalse() {
        await using var actor = new BackpressureTestActor();
        actor.IsInputHighWatermark.Should().BeFalse();
        actor.IsInputCriticalWatermark.Should().BeFalse();
    }

    // ===== 守卫补全:构造函数取值范围边界值(确定性测试,不依赖时序/IO) =====

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_MaxRetries_Zero_ThrowsArgumentOutOfRangeException() {
        var act = () => new ActorBackpressure(Capacity: 100, MaxRetries: 0);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*MaxRetries*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_MaxRetries_Negative_ThrowsArgumentOutOfRangeException() {
        var act = () => new ActorBackpressure(Capacity: 100, MaxRetries: -1);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*MaxRetries*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_RetryQueueCapacity_Zero_ThrowsArgumentOutOfRangeException() {
        var act = () => new ActorBackpressure(Capacity: 100, RetryQueueCapacity: 0);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*RetryQueueCapacity*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_RetryQueueCapacity_Negative_ThrowsArgumentOutOfRangeException() {
        var act = () => new ActorBackpressure(Capacity: 100, RetryQueueCapacity: -1);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*RetryQueueCapacity*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_HighWatermark_Negative_ThrowsArgumentOutOfRangeException() {
        var act = () => new ActorBackpressure(Capacity: 100, HighWatermark: -1);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*HighWatermark*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_HighWatermark_GreaterThanCapacity_ThrowsArgumentOutOfRangeException() {
        var act = () => new ActorBackpressure(Capacity: 100, HighWatermark: 101);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*HighWatermark*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_CriticalWatermark_Negative_ThrowsArgumentOutOfRangeException() {
        var act = () => new ActorBackpressure(Capacity: 100, CriticalWatermark: -1);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*CriticalWatermark*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_CriticalWatermark_GreaterThanCapacity_ThrowsArgumentOutOfRangeException() {
        var act = () => new ActorBackpressure(Capacity: 100, CriticalWatermark: 101);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*CriticalWatermark*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_HighWatermark_GreaterOrEqualCriticalWatermark_ThrowsArgumentException() {
        var actEqual = () => new ActorBackpressure(Capacity: 100, HighWatermark: 90, CriticalWatermark: 90);
        actEqual.Should().Throw<ArgumentException>().WithMessage("*必须 < CriticalWatermark*");

        var actGreater = () => new ActorBackpressure(Capacity: 100, HighWatermark: 95, CriticalWatermark: 80);
        actGreater.Should().Throw<ArgumentException>().WithMessage("*必须 < CriticalWatermark*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_ValidArguments_DoesNotThrow() {
        var bp = new ActorBackpressure(Capacity: 100, HighWatermark: 80, CriticalWatermark: 90, MaxRetries: 16, RetryQueueCapacity: 1024);
        bp.Capacity.Should().Be(100);
        bp.MaxRetries.Should().Be(16);
        bp.RetryQueueCapacity.Should().Be(1024);
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_CapacityZero_WithNullWatermarks_DoesNotThrow() {
        // 无界通道(Capacity=0)+ null 水位线是合法用法,不应抛异常
        var bp = new ActorBackpressure(Capacity: 0);
        bp.Capacity.Should().Be(0);
        bp.EffectiveHighWatermark.Should().Be(0);
        bp.EffectiveCriticalWatermark.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void Constructor_DefaultArguments_DoesNotThrow() {
        var bp = new ActorBackpressure(Capacity: 100);
        bp.MaxRetries.Should().Be(16);
        bp.RetryQueueCapacity.Should().Be(1024);
        bp.EffectiveHighWatermark.Should().Be(80);
        bp.EffectiveCriticalWatermark.Should().Be(95);
    }
}

/// <summary>
/// 背压测试用 Actor — 支持 gate(门控暂停 Consumer)和背压构造函数。
/// </summary>
internal sealed class BackpressureTestActor : ActorBase<BackpressureTestActor.ICommand, Unit> {
    internal interface ICommand;

    internal sealed record IncrementCommand(TaskCompletionSource<int> Tcs) : ICommand;
    internal sealed record ReleaseGateCommand : ICommand;

    private int _value;
    private TaskCompletionSource _gate = CreateCompletedGate();

    private static TaskCompletionSource CreateCompletedGate() {
        var tcs = new TaskCompletionSource();
        tcs.SetResult();
        return tcs;
    }

    public BackpressureTestActor(int? boundedCapacity = null) : base(boundedCapacity) { }

    public BackpressureTestActor(ActorBackpressure backpressure) : base(backpressure) { }

    public void SetGate(TaskCompletionSource gate) {
        _gate = gate;
    }

    protected override void Handle(ICommand command, CancellationToken ct) {
        switch (command) {
            case IncrementCommand(var tcs):
            _gate.Task.WaitAsync(ct).GetAwaiter().GetResult();
            _value++;
            tcs.TrySetResult(_value);
            return;
            case ReleaseGateCommand:
            _gate.TrySetResult();
            return;
            default:
            return;
        }
    }

    public ValueTask IncrementAsync(TaskCompletionSource<int> tcs) { Tell(new IncrementCommand(tcs)); return ValueTask.CompletedTask; }
    public ValueTask ReleaseGateAsync() { Tell(new ReleaseGateCommand()); return ValueTask.CompletedTask; }
}