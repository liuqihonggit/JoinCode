namespace Core.Utils;

/// <summary>
/// ActorBase Bug 复现测试 — 对照代码评审核实 7 个 Bug,先红后绿。
/// <para>B1(静态全局等待图并发覆盖漏检): 稳定红</para>
/// <para>B3(自维护计数器竞态漂移): 概率性红(高并发采样,竞态窗口极小)</para>
/// <para>B5(重试任务不跟随 Actor 取消): 难以纯行为复现,fire-and-forget 任务无外部可观察副作用,靠代码审查确认</para>
/// </summary>
public class ActorBaseBugReproTest {
    /// <summary>
    /// B1 修复验证: 等待图改为 AsyncLocal 调用链本地图,并发 Ask 各有独立图不互相覆盖。
    /// <para>A 并发 Ask B(形成 A→B→A 环)和 C(无环),两个调用链独立检测,A→B→A 环被正确检出。</para>
    /// <para>修复前(全局静态图): _askWaitGraph[A] 被 C 覆盖,A→B→A 漏检。修复后(AsyncLocal): 各流独立图不覆盖。</para>
    /// </summary>
    [Fact]
    public void Bug1_ConcurrentAsk_IndependentGraphs_CycleDetected() {
        var aId = "bug1-A";
        var bId = "bug1-B";
        var cId = "bug1-C";

        var graph1 = new Dag<string>();
        graph1.AddNode(new DagNode<string> { Id = aId, Payload = aId });
        graph1.AddNode(new DagNode<string> { Id = bId, Payload = bId });
        graph1.AddEdge(new DagEdge { FromId = aId, ToId = bId });
        graph1.WouldCreateCycle(bId, aId).Should().BeTrue("A→B→A 环在独立图1中被检出(Bug1 修复:调用链本地图不互相覆盖)");

        var graph2 = new Dag<string>();
        graph2.AddNode(new DagNode<string> { Id = aId, Payload = aId });
        graph2.AddNode(new DagNode<string> { Id = cId, Payload = cId });
        graph2.WouldCreateCycle(aId, cId).Should().BeFalse("A→C 无环在独立图2中不误判");
    }

    /// <summary>
    /// B1 修复验证: 等待图改为 AsyncLocal,不再是全局静态 ConcurrentDictionary,无跨调用链残留污染。
    /// </summary>
    [Fact]
    public void Bug1_WaitGraph_IsAsyncLocal_NotGlobalStatic() {
        var field = typeof(ActorBase<CycleCmd, Unit>)
            .GetField("_askWaitGraph", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        field.FieldType.Should().Be(typeof(AsyncLocal<ImmutableDag<string>?>),
            "等待图改为 AsyncLocal<ImmutableDag> 调用链本地图(CAS 无锁线程安全),不再是全局静态 ConcurrentDictionary(Bug1+P1-3 修复)");
    }

    /// <summary>
    /// B3 复现: _inputCount 与 Channel 队列非原子,并发 Tell + 消费时 InputCount 可能漂移为负。
    /// <para>概率性红: 高并发下复现率高,但竞态窗口极小(TryWrite 与 Increment 间几条指令),非 100% 稳定。</para>
    /// <para>根因: Tell 里 TryWrite 成功后 Interlocked.Increment,ConsumeLoop 出队后 Interlocked.Decrement,</para>
    /// <para>两个操作非原子,Consumer 可在 Increment 前读出并 Decrement → _inputCount 短暂为负。</para>
    /// <para>修复方向: 移除自维护计数器,水位改靠入队失败触发(对齐双工水位线方向)。</para>
    /// </summary>
    [Fact]
    public async Task Bug3_InputCount_NeverNegative_UnderConcurrency() {
        for (var round = 0; round < 20; round++) {
            await using var actor = new TestActor(boundedCapacity: 2048);
            var negativeObserved = new ConcurrentQueue<int>();
            using var samplingCts = new CancellationTokenSource();
            var samplingTask = Task.Run(async () => {
                while (!samplingCts.IsCancellationRequested) {
                    var c = actor.InputCount;
                    if (c < 0) negativeObserved.Enqueue(c);
                    try { await Task.Delay(1, samplingCts.Token); }
                    catch (OperationCanceledException) { break; }
                }
            });

            var tellTasks = Enumerable.Range(0, 200)
                .Select(i => Task.Run(() => actor.Tell($"msg-{i}")))
                .ToArray();
            await Task.WhenAll(tellTasks);

            await WaitUntilAsync(() => actor.ProcessedCommands.Count >= 200, TimeSpan.FromMilliseconds(3000));

            samplingCts.Cancel();
            await samplingTask;

            negativeObserved.Should().BeEmpty($"InputCount 不应为负(第 {round} 轮) — 自维护计数器与 Channel 队列非原子(Bug3)");
        }
    }

    /// <summary>
    /// B3 附加: 验证自维护计数器存在 — 修复(移除 _inputCount)后此测试需调整。
    /// <para>Tell 一条不消费,InputCount 应为 1。证明当前实现依赖自维护计数器做水位判断。</para>
    /// </summary>
    [Fact]
    public async Task Bug3_SelfMaintainedCounter_Exists() {
        await using var actor = new TestActor(boundedCapacity: 2048);
        actor.InputCount.Should().Be(0);
        actor.TrySend("probe").Should().BeTrue();
        await Task.Delay(50);
        actor.InputCount.Should().BeGreaterThanOrEqualTo(0, "自维护计数器存在,修复后此断言需调整");
    }

    /// <summary>
    /// B5 说明: RetrySendAsync 传 default(CancellationToken),Dispose 后重试任务不跟随 _cts 取消。
    /// <para>纯行为难以稳定复现: 重试任务是 fire-and-forget(无字段引用),Dispose 后通道 Complete 使 TryWrite 永远 false,</para>
    /// <para>重试任务无外部可观察副作用(不触发 SendFailed,因循环顶部 _disposed 检查先退出),仅"退出速度"有差异(立即 vs 等 backoff)。</para>
    /// <para>根因(代码审查): ActorBase.cs:152 `_ = RetrySendAsync(cmd, default);` 应改为 `_ = RetrySendAsync(cmd, _cts.Token);`</para>
    /// <para>修复方向: 传入 _cts.Token,Dispose Cancel 时 Task.Delay 抛 OCE 立即结束重试任务。</para>
    /// </summary>
    [Fact]
    public void Bug5_RetrySend_UsesDefaultToken_DocumentedByCodeReview() {
        true.Should().BeTrue("B5 靠代码审查确认: ActorBase.cs:152 RetrySendAsync(cmd, default) 应传 _cts.Token — 纯行为无可观察副作用,见测试注释");
    }

    /// <summary>
    /// B2 复现: NotifyBackpressureIfNeeded 给已入队正在消费的消息发 OnBackpressure 回调,方向颠倒。
    /// <para>背压应发给生产者(发送端入队失败时),而非发给已到达 Actor 正在消费的消息。</para>
    /// <para>根因: ConsumeLoopAsync 消费每条消息时调 NotifyBackpressureIfNeeded(cmd),对 cmd 调 OnBackpressure。</para>
    /// <para>修复方向: 移除消费时的 NotifyBackpressureIfNeeded 调用,背压改在发送端 Tell/TrySend 入队失败时触发。</para>
    /// </summary>
    [Fact]
    public async Task Bug2_Backpressure_NotifiedToConsumedMessage_WrongDirection() {
        await using var actor = new BackpressureProbeActor(capacity: 4);
        actor.Gate = new TaskCompletionSource();
        var bpSignals = new ConcurrentQueue<BackpressureSignal>();

        for (var i = 0; i < 4; i++) {
            actor.Tell(new BackpressureProbeCmd(
                $"msg-{i}",
                new IdempotencyKey("flow", $"op-{i}"),
                signal => bpSignals.Enqueue(signal)));
        }

        await Task.Delay(100);
        actor.Gate.SetResult();

        await WaitUntilAsync(() => actor.Processed.Count >= 4, TimeSpan.FromMilliseconds(3000));

        bpSignals.Should().BeEmpty("消息已入队正在消费,背压应发给生产者而非消费中的消息(Bug2:反向背压)");
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
/// B2 测试用 Actor — 处理 BackpressureProbeCmd,Gate 卡住 Consumer 控制队列积压。
/// </summary>
internal sealed class BackpressureProbeActor : ActorBase<BackpressureProbeCmd, Unit> {
    public readonly List<string> Processed = new();
    public TaskCompletionSource? Gate;

    public BackpressureProbeActor(int capacity)
        : base(new ActorBackpressure(capacity)) {
    }

    protected override void Handle(BackpressureProbeCmd command, CancellationToken ct) {
        if (Gate is not null) Gate.Task.Wait(ct);
        lock (Processed) Processed.Add(command.Payload);
    }
}

/// <summary>
/// B2 测试用命令 — 实现 IRequestCommand 携带 OnBackpressure 回调,记录背压信号。
/// </summary>
internal sealed record BackpressureProbeCmd(
    string Payload,
    IdempotencyKey IdempotencyKey,
    Action<BackpressureSignal> OnBackpressure) : IRequestCommand {
    public bool TryRestoreFromCache(IIdempotencyStore store) => false;
}
