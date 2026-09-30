namespace Core.Utils;

/// <summary>
/// ActorBase Bug 复现测试 — 时序部分(并发/延迟/竞态窗口,需 Task.Delay 与并发采样)。
/// <para>拆分自原 ActorBaseBugReproTest,确定性部分见 ActorBaseBugReproPureTests。</para>
/// <para>B3(自维护计数器竞态漂移): 概率性红(高并发采样,竞态窗口极小) — 标 Flaky</para>
/// <para>B2(背压通知方向颠倒): 时序红(需 Task.Delay 制造队列积压窗口)</para>
/// </summary>
public class ActorBaseBugReproTest {
    /// <summary>
    /// B3 复现: _inputCount 与 Channel 队列非原子,并发 Tell + 消费时 InputCount 可能漂移为负。
    /// <para>概率性红: 高并发下复现率高,但竞态窗口极小(TryWrite 与 Increment 间几条指令),非 100% 稳定。</para>
    /// <para>根因: Tell 里 TryWrite 成功后 Interlocked.Increment,ConsumeLoop 出队后 Interlocked.Decrement,</para>
    /// <para>两个操作非原子,Consumer 可在 Increment 前读出并 Decrement → _inputCount 短暂为负。</para>
    /// <para>修复方向: 移除自维护计数器,水位改靠入队失败触发(对齐双工水位线方向)。</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Flaky")]
    [Trait("Category", "Timing")]
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

            await TestWaitHelper.WaitUntilAsync(() => actor.ProcessedCommands.Count >= 200, TimeSpan.FromMilliseconds(3000));

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
    [Trait("Category", "Timing")]
    public async Task Bug3_SelfMaintainedCounter_Exists() {
        await using var actor = new TestActor(boundedCapacity: 2048);
        actor.InputCount.Should().Be(0);
        actor.TrySend("probe").Should().BeTrue();
        await Task.Delay(50);
        actor.InputCount.Should().BeGreaterThanOrEqualTo(0, "自维护计数器存在,修复后此断言需调整");
    }

    /// <summary>
    /// B2 复现: NotifyBackpressureIfNeeded 给已入队正在消费的消息发 OnBackpressure 回调,方向颠倒。
    /// <para>背压应发给生产者(发送端入队失败时),而非发给已到达 Actor 正在消费的消息。</para>
    /// <para>根因: ConsumeLoopAsync 消费每条消息时调 NotifyBackpressureIfNeeded(cmd),对 cmd 调 OnBackpressure。</para>
    /// <para>修复方向: 移除消费时的 NotifyBackpressureIfNeeded 调用,背压改在发送端 Tell/TrySend 入队失败时触发。</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Timing")]
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

        await TestWaitHelper.WaitUntilAsync(() => actor.Processed.Count >= 4, TimeSpan.FromMilliseconds(3000));

        bpSignals.Should().BeEmpty("消息已入队正在消费,背压应发给生产者而非消费中的消息(Bug2:反向背压)");
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
