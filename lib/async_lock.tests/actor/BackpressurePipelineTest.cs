namespace Core.Utils;

/// <summary>
/// 环形背压管道测试 — 验证 ConsumeLoop 水位线变化时强制通知 OnBackpressure + CreateBackpressureHandler 延迟重试。
/// </summary>
public class BackpressurePipelineTest {
    /// <summary>高水位线触发 OnBackpressure(Level=High)</summary>
    [Fact]
    public async Task HighWatermark_TriggersOnBackpressure_HighLevel() {
        var gate = new TaskCompletionSource();
        await using var actor = new BackpressurePipelineTestActor(10, gate);

        var signals = new List<BackpressureSignal>();
        for (var i = 0; i < 9; i++) {
            var cmd = BackpressureTestCmd.Create(
                new IdempotencyKey("bp-test", i.ToString()),
                signals);
            actor.Tell(cmd);
        }

        await WaitUntilAsync(() => signals.Any(s => s.Level == WatermarkLevel.High), TimeSpan.FromSeconds(1));
        signals.Should().Contain(s => s.Level == WatermarkLevel.High);
    }

    /// <summary>危险水位线触发 OnBackpressure(Level=Critical)</summary>
    [Fact]
    public async Task CriticalWatermark_TriggersOnBackpressure_CriticalLevel() {
        var gate = new TaskCompletionSource();
        await using var actor = new BackpressurePipelineTestActor(10, gate);

        var signals = new List<BackpressureSignal>();
        for (var i = 0; i < 10; i++) {
            var cmd = BackpressureTestCmd.Create(
                new IdempotencyKey("bp-test", i.ToString()),
                signals);
            actor.Tell(cmd);
        }

        await WaitUntilAsync(() => signals.Any(s => s.Level == WatermarkLevel.Critical), TimeSpan.FromSeconds(1));
        signals.Should().Contain(s => s.Level == WatermarkLevel.Critical);
    }

    /// <summary>水位线恢复到 Normal 时触发 OnBackpressure(Level=Normal)</summary>
    [Fact]
    public async Task WatermarkRecovery_TriggersOnBackpressure_NormalLevel() {
        var gate = new TaskCompletionSource();
        await using var actor = new BackpressurePipelineTestActor(10, gate);

        var signals = new List<BackpressureSignal>();
        for (var i = 0; i < 9; i++) {
            var cmd = BackpressureTestCmd.Create(
                new IdempotencyKey("bp-test", i.ToString()),
                signals);
            actor.Tell(cmd);
        }

        await WaitUntilAsync(() => signals.Any(s => s.Level == WatermarkLevel.High), TimeSpan.FromSeconds(1));
        gate.SetResult();
        await WaitUntilAsync(() => signals.Any(s => s.Level == WatermarkLevel.Normal), TimeSpan.FromSeconds(2));
        signals.Should().Contain(s => s.Level == WatermarkLevel.Normal);
    }

    /// <summary>CreateBackpressureHandler — High 延迟后重试</summary>
    [Fact]
    public async Task CreateBackpressureHandler_High_DelayedRetry() {
        var retried = false;
        var handler = ActorBase<object, Unit>.CreateBackpressureHandler(() => retried = true);
        handler(new BackpressureSignal(0, "", "", WatermarkLevel.High, TimeSpan.FromMilliseconds(50), 0));
        retried.Should().BeFalse();
        await Task.Delay(100);
        retried.Should().BeTrue();
    }

    /// <summary>CreateBackpressureHandler — Critical 延迟后重试</summary>
    [Fact]
    public async Task CreateBackpressureHandler_Critical_DelayedRetry() {
        var retried = false;
        var handler = ActorBase<object, Unit>.CreateBackpressureHandler(() => retried = true);
        handler(new BackpressureSignal(0, "", "", WatermarkLevel.Critical, TimeSpan.FromMilliseconds(200), 0));
        retried.Should().BeFalse();
        await Task.Delay(100);
        retried.Should().BeFalse();
        await Task.Delay(150);
        retried.Should().BeTrue();
    }

    /// <summary>CreateBackpressureHandler — Normal 立即重试</summary>
    [Fact]
    public async Task CreateBackpressureHandler_Normal_ImmediateRetry() {
        var retried = false;
        var handler = ActorBase<object, Unit>.CreateBackpressureHandler(() => retried = true);
        handler(new BackpressureSignal(0, "", "", WatermarkLevel.Normal, TimeSpan.Zero, 0));
        await Task.Yield();
        retried.Should().BeTrue();
    }

    /// <summary>非 IRequestCommand 的普通命令不触发背压通知</summary>
    [Fact]
    public async Task PlainCommand_NoBackpressureNotification() {
        var gate = new TaskCompletionSource();
        await using var actor = new BackpressurePipelineTestActor(10, gate);

        var signals = new List<BackpressureSignal>();
        for (var i = 0; i < 10; i++)
            actor.Tell(new PlainCmd(i));

        await Task.Delay(200);
        signals.Should().BeEmpty();
        gate.SetResult();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout) {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline) {
            if (condition()) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"等待条件超时 {timeout.TotalMilliseconds:F0}ms");
    }
}

internal sealed record PlainCmd(int Id);

internal sealed record BackpressureTestCmd(
    IdempotencyKey IdempotencyKey,
    Action<Unit> OnSuccess,
    Action<Exception> OnFailure,
    Action<BackpressureSignal> OnBackpressure
) : IRequestCommand<Unit> {
    public static BackpressureTestCmd Create(IdempotencyKey key, List<BackpressureSignal> signals) {
        return new BackpressureTestCmd(
            key,
            _ => { },
            _ => { },
            signal => { lock (signals) signals.Add(signal); });
    }

    public bool TryRestoreFromCache(IIdempotencyStore store) {
        if (store.TryGetResult<Unit>(IdempotencyKey, out var cached)) {
            OnSuccess(cached);
            return true;
        }
        return false;
    }
}

internal sealed class BackpressurePipelineTestActor : ActorBase<object, Unit> {
    private readonly TaskCompletionSource? _gate;

    public BackpressurePipelineTestActor(int capacity, TaskCompletionSource? gate = null)
        : base(new ActorBackpressure(capacity)) {
        _gate = gate;
        IdempotencyStore = new IdempotencyStore();
    }

    protected override void Handle(object command, CancellationToken ct) {
        _ = HandleAsyncImpl(command, ct);
    }

    private async ValueTask HandleAsyncImpl(object command, CancellationToken ct) {
        if (_gate is not null) await _gate.Task.WaitAsync(ct);
        if (command is BackpressureTestCmd cmd) {
            IdempotencyStore?.TryRegister(cmd.IdempotencyKey, Unit.Value);
            cmd.OnSuccess(Unit.Value);
        }
    }
}
