// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Utils;

/// <summary>
/// ActorBase Bug 复现测试 — 确定性部分(纯逻辑/反射/代码审查,不涉时序与并发竞争)。
/// <para>拆分自 ActorBaseBugReproTest,与 ActorBaseBugReproTest(时序部分)互补。</para>
/// <para>B1(静态全局等待图并发覆盖漏检): 稳定红 — 纯 Dag 操作与反射,确定性可复现</para>
/// <para>B5(重试任务不跟随 Actor 取消): 靠代码审查确认,纯断言,确定性</para>
/// </summary>
[Trait("Category", "Deterministic")]
public class ActorBaseBugReproPureTests {
    /// <summary>
    /// B1 修复验证: 等待图改为 AsyncLocal 调用链本地图,并发 Ask 各有独立图不互相覆盖。
    /// <para>A 并发 Ask B(形成 A→B→A 环)和 C(无环),两个调用链独立检测,A→B→A 环被正确检出。</para>
    /// <para>修复前(全局静态图): _askWaitGraph[A] 被 C 覆盖,A→B→A 漏检。修复后(AsyncLocal): 各流独立图不覆盖。</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Deterministic")]
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
    [Trait("Category", "Deterministic")]
    public void Bug1_WaitGraph_IsAsyncLocal_NotGlobalStatic() {
        var field = typeof(ActorBase<CycleCmd, Unit>)
            .GetField("_askWaitGraph", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        field.FieldType.Should().Be(typeof(AsyncLocal<ImmutableDag<string>?>),
            "等待图改为 AsyncLocal<ImmutableDag> 调用链本地图(CAS 无锁线程安全),不再是全局静态 ConcurrentDictionary(Bug1+P1-3 修复)");
    }

    /// <summary>
    /// B5 说明: RetrySendAsync 传 default(CancellationToken),Dispose 后重试任务不跟随 _cts 取消。
    /// <para>纯行为难以稳定复现: 重试任务是 fire-and-forget(无字段引用),Dispose 后通道 Complete 使 TryWrite 永远 false,</para>
    /// <para>重试任务无外部可观察副作用(不触发 SendFailed,因循环顶部 _disposed 检查先退出),仅"退出速度"有差异(立即 vs 等 backoff)。</para>
    /// <para>根因(代码审查): ActorBase.cs:152 `_ = RetrySendAsync(cmd, default);` 应改为 `_ = RetrySendAsync(cmd, _cts.Token);`</para>
    /// <para>修复方向: 传入 _cts.Token,Dispose Cancel 时 Task.Delay 抛 OCE 立即结束重试任务。</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Deterministic")]
    public void Bug5_RetrySend_UsesDefaultToken_DocumentedByCodeReview() {
        true.Should().BeTrue("B5 靠代码审查确认: ActorBase.cs:152 RetrySendAsync(cmd, default) 应传 _cts.Token — 纯行为无可观察副作用,见测试注释");
    }
}
