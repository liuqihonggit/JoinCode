namespace Core.Utils;

/// <summary>
/// ConsumeLoopAsync 确定性单元测试 — 验证 ProcessSingleCommand 单条命令处理逻辑,不依赖 Consumer 调度时序。
/// <para>时序分离:ConsumeLoopAsync 只负责循环编排(时序),ProcessSingleCommand 负责单条命令处理(确定性)。</para>
/// <para>覆盖:普通命令/幂等命中跳过/幂等未命中执行/Handle异常/OnConsumerError异常/取消退出/取消未退出。</para>
/// <para>测试直接调用 ProcessSingleCommand,无需 Tell+等待 Consumer 调度,完全确定性。</para>
/// </summary>
public class ConsumeLoopPureFunctionsTest {

    /// <summary>普通命令(无幂等存储) → Handle 被调用,返回 true(继续循环)</summary>
    [Fact]
    public async Task NormalCommand_HandleCalled_ReturnsTrue() {
        await using var actor = new ProcessCommandTestActor();
        var cmd = new ProcessCmd(restoreResult: false);

        var result = actor.ProcessSingleCommand(cmd, CancellationToken.None);

        result.Should().BeTrue("普通命令处理完成,继续循环");
        actor.HandleCount.Should().Be(1);
        actor.ErrorCount.Should().Be(0);
    }

    /// <summary>幂等命中(TryRestoreFromCache=true) → Handle 不调用,返回 true(跳过处理)</summary>
    [Fact]
    public async Task IdempotentHit_SkipsHandle_ReturnsTrue() {
        var store = new MockIdempotencyStore();
        await using var actor = new ProcessCommandTestActor(store);
        var cmd = new ProcessCmd(restoreResult: true);

        var result = actor.ProcessSingleCommand(cmd, CancellationToken.None);

        result.Should().BeTrue("幂等命中跳过 Handle,继续循环");
        actor.HandleCount.Should().Be(0, "幂等命中,Handle 不应调用");
        cmd.RestoreCallCount.Should().Be(1, "TryRestoreFromCache 应调用一次");
    }

    /// <summary>幂等未命中(TryRestoreFromCache=false) → Handle 调用,返回 true</summary>
    [Fact]
    public async Task IdempotentMiss_ExecutesHandle_ReturnsTrue() {
        var store = new MockIdempotencyStore();
        await using var actor = new ProcessCommandTestActor(store);
        var cmd = new ProcessCmd(restoreResult: false);

        var result = actor.ProcessSingleCommand(cmd, CancellationToken.None);

        result.Should().BeTrue("幂等未命中,执行 Handle,继续循环");
        actor.HandleCount.Should().Be(1, "幂等未命中,Handle 应调用");
        cmd.RestoreCallCount.Should().Be(1, "TryRestoreFromCache 应调用一次");
    }

    /// <summary>Handle 抛普通异常 → OnConsumerError 调用,返回 true(不中断循环)</summary>
    [Fact]
    public async Task HandleThrows_OnConsumerErrorCalled_ReturnsTrue() {
        await using var actor = new ProcessCommandTestActor();
        actor.SetThrowInHandle(true);
        var cmd = new ProcessCmd(restoreResult: false);

        var result = actor.ProcessSingleCommand(cmd, CancellationToken.None);

        result.Should().BeTrue("Handle 异常被捕获,不中断循环");
        actor.HandleCount.Should().Be(1, "Handle 被调用(虽然抛异常)");
        actor.ErrorCount.Should().Be(1, "OnConsumerError 应调用一次");
    }

    /// <summary>Handle + OnConsumerError 都抛异常 → 异常吞掉,返回 true(不中断循环)</summary>
    [Fact]
    public async Task HandleAndOnErrorBothThrow_SwallowsException_ReturnsTrue() {
        await using var actor = new ProcessCommandTestActor();
        actor.SetThrowInHandle(true);
        actor.SetThrowInOnError(true);
        var cmd = new ProcessCmd(restoreResult: false);

        var result = actor.ProcessSingleCommand(cmd, CancellationToken.None);

        result.Should().BeTrue("OnConsumerError 异常被吞掉,不中断循环");
        actor.HandleCount.Should().Be(1);
        actor.ErrorCount.Should().Be(1, "OnConsumerError 被调用(虽然抛异常)");
    }

    /// <summary>OperationCanceledException + ct 已取消 → 返回 false(退出循环信号)</summary>
    [Fact]
    public async Task OperationCanceledWithCancellation_ReturnsFalse() {
        await using var actor = new ProcessCommandTestActor();
        actor.SetThrowOperationCanceled(true);
        var cmd = new ProcessCmd(restoreResult: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = actor.ProcessSingleCommand(cmd, cts.Token);

        result.Should().BeFalse("Actor 关闭(ct 已取消),退出循环");
        actor.HandleCount.Should().Be(1, "Handle 被调用(抛 OperationCanceledException)");
        actor.ErrorCount.Should().Be(0, "OperationCanceled+ct取消 不走 OnConsumerError");
    }

    /// <summary>OperationCanceledException + ct 未取消 → 当普通异常处理,返回 true</summary>
    [Fact]
    public async Task OperationCanceledWithoutCancellation_ReturnsTrue() {
        await using var actor = new ProcessCommandTestActor();
        actor.SetThrowOperationCanceled(true);
        var cmd = new ProcessCmd(restoreResult: false);

        var result = actor.ProcessSingleCommand(cmd, CancellationToken.None);

        result.Should().BeTrue("ct 未取消,OperationCanceled 当普通异常,不退出循环");
        actor.HandleCount.Should().Be(1);
        actor.ErrorCount.Should().Be(1, "当普通异常走 OnConsumerError");
    }

    /// <summary>无幂等存储(null) → 所有命令都执行 Handle(幂等守卫不启用)</summary>
    [Fact]
    public async Task NoIdempotencyStore_AlwaysExecutesHandle() {
        await using var actor = new ProcessCommandTestActor(store: null);
        var cmd1 = new ProcessCmd(restoreResult: true);
        var cmd2 = new ProcessCmd(restoreResult: false);

        actor.ProcessSingleCommand(cmd1, CancellationToken.None);
        actor.ProcessSingleCommand(cmd2, CancellationToken.None);

        actor.HandleCount.Should().Be(2, "无幂等存储,两条命令都执行 Handle");
        cmd1.RestoreCallCount.Should().Be(0, "无存储,TryRestoreFromCache 不调用");
        cmd2.RestoreCallCount.Should().Be(0, "无存储,TryRestoreFromCache 不调用");
    }

    /// <summary>连续处理多条命令 → 每条独立处理,HandleCount 累加</summary>
    [Fact]
    public async Task MultipleCommands_EachProcessedIndependently() {
        await using var actor = new ProcessCommandTestActor();

        for (var i = 0; i < 5; i++) {
            var result = actor.ProcessSingleCommand(new ProcessCmd(false), CancellationToken.None);
            result.Should().BeTrue($"第 {i + 1} 条命令应返回 true");
        }

        actor.HandleCount.Should().Be(5, "5 条命令各处理一次");
    }
}

/// <summary>ProcessSingleCommand 测试用 Actor — 记录 Handle/OnConsumerError 调用,可控制异常行为</summary>
internal sealed class ProcessCommandTestActor : ActorBase<ProcessCmd, Unit> {
    private int _handleCount;
    private int _errorCount;
    private volatile bool _throwInHandle;
    private volatile bool _throwInOnError;
    private volatile bool _throwOperationCanceled;

    public int HandleCount => Volatile.Read(ref _handleCount);
    public int ErrorCount => Volatile.Read(ref _errorCount);

    public void SetThrowInHandle(bool v) => _throwInHandle = v;
    public void SetThrowInOnError(bool v) => _throwInOnError = v;
    public void SetThrowOperationCanceled(bool v) => _throwOperationCanceled = v;

    public ProcessCommandTestActor(IIdempotencyStore? store = null) : base(idempotencyStore: store) { }

    protected override void Handle(ProcessCmd command, CancellationToken ct) {
        Interlocked.Increment(ref _handleCount);
        if (_throwOperationCanceled) throw new OperationCanceledException();
        if (_throwInHandle) throw new InvalidOperationException("test-handle-error");
    }

    protected override void OnConsumerError(Exception ex) {
        Interlocked.Increment(ref _errorCount);
        if (_throwInOnError) throw new InvalidOperationException("test-onerror-error");
    }
}

/// <summary>测试命令 — 实现 IRequestCommand,可控 TryRestoreFromCache 返回值</summary>
internal sealed class ProcessCmd : IRequestCommand {
    private readonly bool _restoreResult;
    private int _restoreCallCount;

    public ProcessCmd(bool restoreResult = false) {
        _restoreResult = restoreResult;
        IdempotencyKey = new IdempotencyKey("test-flow", "test-op");
        OnBackpressure = _ => { };
    }

    public int RestoreCallCount => Volatile.Read(ref _restoreCallCount);
    public IdempotencyKey IdempotencyKey { get; }
    public Action<BackpressureSignal> OnBackpressure { get; }

    public bool TryRestoreFromCache(IIdempotencyStore store) {
        Interlocked.Increment(ref _restoreCallCount);
        return _restoreResult;
    }
}

/// <summary>Mock 幂等存储 — 简单实现,仅供 ProcessSingleCommand 测试触发幂等路径</summary>
internal sealed class MockIdempotencyStore : IIdempotencyStore {
    public bool TryRegister<T>(IdempotencyKey key, T result) => true;
    public bool TryGetResult<T>(IdempotencyKey key, out T? result) { result = default; return false; }
    public bool IsRegistered(IdempotencyKey key) => false;
    public void Evict(IdempotencyKey key) { }
    public int EvictExpired(TimeSpan ttl) => 0;
}
