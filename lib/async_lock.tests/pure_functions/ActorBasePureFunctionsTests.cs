namespace Core.Utils;

/// <summary>
/// 测试用 Actor — 暴露 Handle 供测试观察。
/// </summary>
internal sealed class PureFuncTestActor : ActorBase<string, string> {
    public int HandleCallCount { get; private set; }
    public string? LastHandledCommand { get; private set; }

    protected override void Handle(string command, CancellationToken ct) {
        HandleCallCount++;
        LastHandledCommand = command;
    }
}

/// <summary>
/// 幂等测试命令 — 实现 IRequestCommand,携带幂等键+缓存恢复。
/// </summary>
internal sealed class PureFuncTestRequestCommand : IRequestCommand<string> {
    private static int _restoreCallCount;
    public static int RestoreCallCount => _restoreCallCount;

    public IdempotencyKey IdempotencyKey { get; }
    public Action<BackpressureSignal> OnBackpressure { get; } = _ => { };
    public Action<string> OnSuccess { get; }
    public Action<Exception> OnFailure { get; } = _ => { };
    public string Result { get; }

    public PureFuncTestRequestCommand(IdempotencyKey key, string result, Action<string>? onSuccess = null) {
        IdempotencyKey = key;
        Result = result;
        OnSuccess = onSuccess ?? (_ => { });
    }

    public bool TryRestoreFromCache(IIdempotencyStore store) {
        if (store.TryGetResult(IdempotencyKey, out string? cached)) {
            Interlocked.Increment(ref _restoreCallCount);
            OnSuccess(cached!);
            return true;
        }
        return false;
    }

    public static void ResetRestoreCount() => Interlocked.Exchange(ref _restoreCallCount, 0);
}

public class ActorBaseComputeBackoffTests {
    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 200)]
    [InlineData(2, 400)]
    [InlineData(3, 800)]
    [InlineData(4, 1600)]
    [InlineData(5, 3200)]
    [InlineData(6, 5000)]
    [InlineData(10, 5000)]
    [InlineData(11, 5000)]
    [InlineData(16, 5000)]
    [InlineData(100, 5000)]
    public void ComputeBackoffDelayMs_normal_and_clamped(int attempt, int expectedMs) {
        ActorBase<object, object>.ComputeBackoffDelayMs(attempt).Should().Be(expectedMs);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(-100, 100)]
    public void ComputeBackoffDelayMs_negative_returns_base(int attempt, int expectedMs) {
        ActorBase<object, object>.ComputeBackoffDelayMs(attempt).Should().Be(expectedMs);
    }
}

public class ActorBaseComputeTotalTimeoutTests {
    [Theory]
    [InlineData(1000, 0, 1000)]
    [InlineData(1000, 1, 2000)]
    [InlineData(1000, 16, 17000)]
    [InlineData(5000, 3, 20000)]
    [InlineData(0, 16, 0)]
    public void ComputeTotalTimeoutMs_formula(int singleTimeout, int maxRetries, int expected) {
        ActorBase<object, object>.ComputeTotalTimeoutMs(singleTimeout, maxRetries).Should().Be(expected);
    }
}

public class ActorBaseIsIdempotentCommandTests {
    internal sealed class IdempotentCmd : IIdempotent { }
    internal sealed class NonIdempotentCmd { }

    [Fact]
    public void IIdempotent_marker_returns_true() {
        ActorBase<IdempotentCmd, object>.IsIdempotentCommand(new IdempotentCmd()).Should().BeTrue();
    }

    [Fact]
    public void Non_IIdempotent_returns_false() {
        ActorBase<object, object>.IsIdempotentCommand(new object()).Should().BeFalse();
    }
}

public class ActorBaseIsTimeoutCancellationTests {
    [Fact]
    public void external_token_not_cancelled_is_timeout() {
        using var cts = new CancellationTokenSource();
        ActorBase<object, object>.IsTimeoutCancellation(cts.Token).Should().BeTrue();
    }

    [Fact]
    public void external_token_cancelled_is_not_timeout() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ActorBase<object, object>.IsTimeoutCancellation(cts.Token).Should().BeFalse();
    }
}

public class ActorBaseShouldDelayRetryTests {
    [Theory]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(-100, false)]
    public void ShouldDelayRetry_threshold(int ms, bool expected) {
        ActorBase<object, object>.ShouldDelayRetry(TimeSpan.FromMilliseconds(ms)).Should().Be(expected);
    }
}

public class ActorBaseWatermarkCheckTests {
    [Fact]
    public void CheckHighWatermark_null_backpressure_returns_false() {
        ActorBase<object, object>.CheckHighWatermark(null, 100).Should().BeFalse();
    }

    [Theory]
    [InlineData(100, 80, true)]
    [InlineData(100, 79, true)]
    [InlineData(80, 80, true)]
    [InlineData(79, 80, false)]
    [InlineData(0, 80, false)]
    public void CheckHighWatermark_threshold(int count, int high, bool expected) {
        var bp = new ActorBackpressure(Capacity: 100, HighWatermark: high);
        ActorBase<object, object>.CheckHighWatermark(bp, count).Should().Be(expected);
    }

    [Fact]
    public void CheckCriticalWatermark_null_backpressure_returns_false() {
        ActorBase<object, object>.CheckCriticalWatermark(null, 100).Should().BeFalse();
    }

    [Theory]
    [InlineData(100, 95, true)]
    [InlineData(100, 94, true)]
    [InlineData(95, 95, true)]
    [InlineData(94, 95, false)]
    [InlineData(0, 95, false)]
    public void CheckCriticalWatermark_threshold(int count, int critical, bool expected) {
        var bp = new ActorBackpressure(Capacity: 100, CriticalWatermark: critical);
        ActorBase<object, object>.CheckCriticalWatermark(bp, count).Should().Be(expected);
    }
}

public class ActorBaseCreateChannelTests {
    [Fact]
    public void CreateInputChannel_null_backpressure_uses_default_capacity() {
        var ch = ActorBase<string, string>.CreateInputChannel(null);
        ch.Should().NotBeNull();
        ch.Reader.Count.Should().Be(0);
    }

    [Fact]
    public void CreateInputChannel_zero_capacity_uses_default_capacity() {
        var bp = new ActorBackpressure(Capacity: 0);
        var ch = ActorBase<string, string>.CreateInputChannel(bp);
        ch.Should().NotBeNull();
        ch.Reader.Count.Should().Be(0);
    }

    [Fact]
    public void CreateInputChannel_custom_capacity_creates_bounded_channel() {
        var bp = new ActorBackpressure(Capacity: 10);
        var ch = ActorBase<string, string>.CreateInputChannel(bp);
        ch.Should().NotBeNull();
        for (var i = 0; i < 10; i++) ch.Writer.TryWrite($"msg{i}").Should().BeTrue();
        ch.Writer.TryWrite("overflow").Should().BeFalse();
        ch.Reader.Count.Should().Be(10);
    }

    [Fact]
    public void CreateOutputChannel_null_capacity_uses_default() {
        var ch = ActorBase<string, string>.CreateOutputChannel(null, BoundedChannelFullMode.DropOldest);
        ch.Should().NotBeNull();
        ch.Reader.Count.Should().Be(0);
    }

    [Fact]
    public void CreateOutputChannel_custom_capacity_creates_bounded_channel() {
        var ch = ActorBase<string, string>.CreateOutputChannel(5, BoundedChannelFullMode.DropOldest);
        ch.Should().NotBeNull();
        for (var i = 0; i < 5; i++) ch.Writer.TryWrite($"msg{i}").Should().BeTrue();
        ch.Reader.Count.Should().Be(5);
    }
}

public class ActorBaseProcessSingleCommandTests {
    [Fact]
    public async Task ProcessSingleCommand_calls_handle_once_for_normal_command() {
        await using var actor = new PureFuncTestActor();
        actor.ProcessSingleCommand("hello", CancellationToken.None).Should().BeTrue();
        actor.HandleCallCount.Should().Be(1);
        actor.LastHandledCommand.Should().Be("hello");
    }

    [Fact]
    public async Task ProcessSingleCommand_returns_false_on_cancellation() {
        await using var actor = new CancellableTestActor();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        actor.ProcessSingleCommand("cancelled", cts.Token).Should().BeFalse();
        actor.HandleCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessSingleCommand_swallows_handle_exception_and_continues() {
        await using var actor = new PureFuncThrowingActor();
        actor.ProcessSingleCommand("boom", CancellationToken.None).Should().BeTrue();
        actor.WasCalled.Should().BeTrue();
    }

    internal sealed class PureFuncThrowingActor : ActorBase<string, string> {
        public bool WasCalled { get; private set; }
        protected override void Handle(string command, CancellationToken ct) {
            WasCalled = true;
            throw new InvalidOperationException("test exception");
        }
    }

    internal sealed class CancellableTestActor : ActorBase<string, string> {
        public int HandleCallCount { get; private set; }
        protected override void Handle(string command, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            HandleCallCount++;
        }
    }
}
