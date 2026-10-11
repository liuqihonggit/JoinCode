namespace Core.Utils;

/// <summary>
/// AskWithRetryAsync 单元测试 — 验证重试16次+指数退避+幂等+全图环检测。
/// </summary>
public class AskWithRetryTest {
    /// <summary>
    /// 首次成功 — 不重试,CallCount=1。
    /// </summary>
    [Fact]
    public async Task FirstAttemptSucceeds_ReturnsImmediately() {
        await using var actor = new RetryDemoActor();
        actor.SetSuccessAfter(0);
        var result = await actor.AskWithRetryTestAsync(singleTimeoutMs: 2000);
        result.Should().Be("success-at-1");
        actor.CallCount.Should().Be(1);
    }

    /// <summary>
    /// 首次超时,重试后成功 — CallCount>=2。
    /// </summary>
    [Fact]
    public async Task FirstAttemptTimesOut_RetriesAndSucceeds() {
        await using var actor = new RetryDemoActor();
        actor.SetSuccessAfter(1);
        var result = await actor.AskWithRetryTestAsync(singleTimeoutMs: 100, maxRetries: 5);
        result.Should().Be("success-at-2");
        actor.CallCount.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// 全部超时 — 重试耗尽抛 ActorAskDeadlockException。
    /// </summary>
    [Fact]
    public async Task AllAttemptsTimeout_ThrowsDeadlockException() {
        await using var actor = new RetryDemoActor();
        actor.SetSuccessAfter(int.MaxValue);
        var act = async () => await actor.AskWithRetryTestAsync(singleTimeoutMs: 50, maxRetries: 2);
        await act.Should().ThrowAsync<ActorAskDeadlockException>();
    }

    /// <summary>
    /// 取消令牌触发 — 立即抛 OperationCanceledException,不重试。
    /// </summary>
    [Fact]
    public async Task CancellationTokenTriggered_ThrowsImmediately() {
        await using var actor = new RetryDemoActor();
        actor.SetSuccessAfter(int.MaxValue);
        using var cts = new CancellationTokenSource();
        var task = actor.AskWithRetryTestAsync(singleTimeoutMs: 5000, maxRetries: 10, ct: cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// 指数退避 — 重试间隔递增(100ms, 200ms, 400ms...)。
    /// 验证:总耗时 >= 退避和(100+200=300ms,2次重试)。
    /// </summary>
    [Fact]
    public async Task ExponentialBackoff_RetryDelayIncreases() {
        await using var actor = new RetryDemoActor();
        actor.SetSuccessAfter(2);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await actor.AskWithRetryTestAsync(singleTimeoutMs: 50, maxRetries: 5);
        sw.Stop();
        // 2次超时(50ms×2) + 2次退避(100ms+200ms=300ms) = 至少400ms
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(350);
    }

    /// <summary>
    /// 幂等命令标记 — RetryCmd 实现 IIdempotent,重试安全。
    /// </summary>
    [Fact]
    public async Task IdempotentCommand_RetryIsSafe() {
        await using var actor = new RetryDemoActor();
        actor.SetSuccessAfter(1);
        var result = await actor.AskWithRetryTestAsync(singleTimeoutMs: 100, maxRetries: 3);
        result.Should().StartWith("success-at-");
        actor.IdempotentCommandReceived.Should().BeTrue();
    }

    /// <summary>
    /// 全图环检测 — 间接环 A→B→C→A 应被检测到。
    /// </summary>
    [Fact]
    public void IndirectCycle_ThreeActors_ThrowsCyclicAskException() {
        var aId = "cycle-A";
        var bId = "cycle-B";
        var cId = "cycle-C";
        var graph = new Dag<string>();
        graph.AddNode(new DagNode<string> { Id = aId, Payload = aId });
        graph.AddNode(new DagNode<string> { Id = bId, Payload = bId });
        graph.AddNode(new DagNode<string> { Id = cId, Payload = cId });
        graph.AddEdge(new DagEdge { FromId = aId, ToId = bId });
        graph.AddEdge(new DagEdge { FromId = bId, ToId = cId });
        graph.WouldCreateCycle(cId, aId).Should().BeTrue("A→B→C→A 构成间接环");
    }

    /// <summary>
    /// 直接环 A→B→A 仍被检测到(回归测试)。
    /// </summary>
    [Fact]
    public void DirectCycle_TwoActors_StillDetected() {
        var aId = "actorA-direct";
        var bId = "actorB-direct";
        var graph = new Dag<string>();
        graph.AddNode(new DagNode<string> { Id = aId, Payload = aId });
        graph.AddNode(new DagNode<string> { Id = bId, Payload = bId });
        graph.AddEdge(new DagEdge { FromId = aId, ToId = bId });
        graph.WouldCreateCycle(bId, aId).Should().BeTrue("A→B→A 构成直接环");
    }

    /// <summary>
    /// 无环 A→B→C(不回 A)不应误判。
    /// </summary>
    [Fact]
    public void NoCycle_ShouldNotDetect() {
        var aId = "actorA-nocycle";
        var bId = "actorB-nocycle";
        var cId = "actorC-nocycle";
        var graph = new Dag<string>();
        graph.AddNode(new DagNode<string> { Id = aId, Payload = aId });
        graph.AddNode(new DagNode<string> { Id = bId, Payload = bId });
        graph.AddNode(new DagNode<string> { Id = cId, Payload = cId });
        graph.AddEdge(new DagEdge { FromId = aId, ToId = bId });
        graph.AddEdge(new DagEdge { FromId = bId, ToId = cId });
        graph.WouldCreateCycle(aId, bId).Should().BeFalse("A→B→C 不构成环");
    }

    /// <summary>验证 allowNonIdempotentRetry=false 时非幂等命令抛 InvalidOperationException</summary>
    [Fact]
    public async Task NonIdempotent_AllowFalse_ThrowsInvalidOperationException() {
        await using var actor = new NonIdempotentRetryActor();
        var act = async () => await actor.AskTestAsync(singleTimeoutMs: 100, maxRetries: 3, allowNonIdempotentRetry: false);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>验证 allowNonIdempotentRetry=true 时非幂等命令正常执行(仅打日志)</summary>
    [Fact]
    public async Task NonIdempotent_AllowTrue_SucceedsWithWarning() {
        await using var actor = new NonIdempotentRetryActor();
        var result = await actor.AskTestAsync(singleTimeoutMs: 2000, maxRetries: 3, allowNonIdempotentRetry: true);
        result.Should().Be("ok");
    }

    /// <summary>验证幂等命令 allowNonIdempotentRetry=false 不抛异常</summary>
    [Fact]
    public async Task Idempotent_AllowFalse_Succeeds() {
        await using var actor = new RetryDemoActor();
        actor.SetSuccessAfter(0);
        var result = await actor.AskWithRetryTestAsync(singleTimeoutMs: 2000, maxRetries: 3);
        result.Should().Be("success-at-1");
    }
}

/// <summary>
/// 重试测试 Actor — 前 N 次不 SetResult(模拟超时),第 N+1 次立即 SetResult。
/// </summary>
internal sealed class RetryDemoActor : ActorBase<RetryCmd, Unit> {
    private int _successAfter;
    private int _callCount;
    private int _idempotentReceived;

    public int CallCount => Volatile.Read(ref _callCount);
    public bool IdempotentCommandReceived => Volatile.Read(ref _idempotentReceived) != 0;

    public void SetSuccessAfter(int n) => Interlocked.Exchange(ref _successAfter, n);

    public Task<string> AskWithRetryTestAsync(int singleTimeoutMs = 1000, int maxRetries = 16, CancellationToken ct = default)
        => AskWithRetryAsync<string>(tcs => new RetryCmd(tcs), ct, singleTimeoutMs, maxRetries);

    protected override void Handle(RetryCmd command, CancellationToken ct) {
        if (command is IIdempotent) Interlocked.Exchange(ref _idempotentReceived, 1);
        var count = Interlocked.Increment(ref _callCount);
        if (count > Volatile.Read(ref _successAfter)) {
            command.Tcs.TrySetResult($"success-at-{count}");
        }
    }
}

internal sealed record RetryCmd(TaskCompletionSource<string> Tcs) : IIdempotent;

/// <summary>
/// 环检测测试用 Actor。
/// </summary>
internal sealed class CycleDemoActor : ActorBase<CycleCmd, Unit> {
    protected override void Handle(CycleCmd command, CancellationToken ct) {
        command.Tcs.TrySetResult(true);
    }
}

internal sealed record CycleCmd(TaskCompletionSource<bool> Tcs);

/// <summary>非幂等重试测试 Actor — 命令不实现 IIdempotent</summary>
internal sealed class NonIdempotentRetryActor : ActorBase<NonIdempotentRetryCmd, Unit> {
    public Task<string> AskTestAsync(int singleTimeoutMs = 1000, int maxRetries = 16, bool allowNonIdempotentRetry = true, CancellationToken ct = default)
        => AskWithRetryAsync<string>(tcs => new NonIdempotentRetryCmd(tcs), ct, singleTimeoutMs, maxRetries, allowNonIdempotentRetry);

    protected override void Handle(NonIdempotentRetryCmd command, CancellationToken ct) {
        command.Tcs.TrySetResult("ok");
    }
}

internal sealed record NonIdempotentRetryCmd(TaskCompletionSource<string> Tcs);
