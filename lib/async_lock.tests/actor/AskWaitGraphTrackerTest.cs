// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Utils;

/// <summary>
/// AskWaitGraphTracker 确定性单元测试 — 验证等待图加边、环检测、作用域恢复,不依赖时序/不启动 Actor。
/// <para>覆盖:无需加边、正常加边、环检测抛异常、嵌套作用域隔离、AsyncLocal 独立、Dispose 恢复父图。</para>
/// </summary>
public class AskWaitGraphTrackerTest {
    /// <summary>callerId=null → 返回 null(无需加边)</summary>
    [Fact]
    public void NullCallerId_ReturnsNull() {
        var al = new AsyncLocal<ImmutableDag<string>?>();
        var tracker = new AskWaitGraphTracker(al);

        using var scope = tracker.EnterScope(null, "B");

        scope.Should().BeNull("callerId 为 null 表示无调用方上下文,无需加边");
    }

    /// <summary>callerId==selfId → 返回 null(自环无需加边)</summary>
    [Fact]
    public void SelfCallerId_ReturnsNull() {
        var al = new AsyncLocal<ImmutableDag<string>?>();
        var tracker = new AskWaitGraphTracker(al);

        using var scope = tracker.EnterScope("A", "A");

        scope.Should().BeNull("callerId 等于自身,自环不加边");
    }

    /// <summary>正常加边 → 返回非 null scope,Dispose 后恢复父图</summary>
    [Fact]
    public void NormalEdge_ReturnsScope_DisposeRestoresParent() {
        var al = new AsyncLocal<ImmutableDag<string>?>();
        var tracker = new AskWaitGraphTracker(al);
        al.Value.Should().BeNull("初始无图");

        using var scope = tracker.EnterScope("A", "B");

        scope.Should().NotBeNull("A→B 加边成功,返回作用域");
        al.Value.Should().NotBeNull("EnterScope 设置新图副本");
        al.Value!.Nodes.ContainsKey("A").Should().BeTrue();
        al.Value.Nodes.ContainsKey("B").Should().BeTrue();
        al.Value.Edges.Values.Should().ContainSingle(e => e.FromId == "A" && e.ToId == "B");

        scope!.Dispose();
        al.Value.Should().BeNull("Dispose 恢复父图(null)");
    }

    /// <summary>环检测:已有 A→B,再加 B→A → 抛 ActorCyclicAskException</summary>
    [Fact]
    public void CycleDetected_ThrowsActorCyclicAskException() {
        var al = new AsyncLocal<ImmutableDag<string>?>();
        var tracker = new AskWaitGraphTracker(al);

        using var scope1 = tracker.EnterScope("A", "B");
        scope1.Should().NotBeNull();

        Action act = () => tracker.EnterScope("B", "A");

        act.Should().Throw<ActorCyclicAskException>("A→B 已存在,加 B→A 形成环 → 循环 Ask 死锁")
            .Which.CallerActorId.Should().Be("B");
        scope1!.Dispose();
    }

    /// <summary>环检测异常的 TargetActorId 正确</summary>
    [Fact]
    public void CycleDetected_ExceptionContainsTargetId() {
        var al = new AsyncLocal<ImmutableDag<string>?>();
        var tracker = new AskWaitGraphTracker(al);
        using var scope1 = tracker.EnterScope("X", "Y");

        var ex = Assert.Throws<ActorCyclicAskException>(() => tracker.EnterScope("Y", "X"));
        ex.TargetActorId.Should().Be("X");
        ex.CallerActorId.Should().Be("Y");
        scope1!.Dispose();
    }

    /// <summary>无环:已有 A→B,加 C→D(独立节点) → 成功</summary>
    [Fact]
    public void NoCycle_IndependentChain_Succeeds() {
        var al = new AsyncLocal<ImmutableDag<string>?>();
        var tracker = new AskWaitGraphTracker(al);

        using var scope1 = tracker.EnterScope("A", "B");
        using var scope2 = tracker.EnterScope("C", "D");

        scope2.Should().NotBeNull("C→D 与 A→B 无环,加边成功");
        al.Value!.Edges.Values.Should().HaveCount(2, "图含 A→B 和 C→D 两条边");
        scope2!.Dispose();
        scope1!.Dispose();
    }

    /// <summary>嵌套作用域隔离:外层 A→B,内层 C→D,Dispose 内层恢复外层图</summary>
    [Fact]
    public void NestedScopes_Isolated_RestoreInOrder() {
        var al = new AsyncLocal<ImmutableDag<string>?>();
        var tracker = new AskWaitGraphTracker(al);

        using var scope1 = tracker.EnterScope("A", "B");
        var graphAfterScope1 = al.Value;
        graphAfterScope1.Should().NotBeNull();

        using var scope2 = tracker.EnterScope("C", "D");
        al.Value.Should().NotBeSameAs(graphAfterScope1, "内层创建新副本,与外层图不同实例");
        al.Value!.Edges.Values.Should().HaveCount(2);

        scope2!.Dispose();
        al.Value.Should().BeSameAs(graphAfterScope1, "内层 Dispose 恢复外层图");

        scope1!.Dispose();
        al.Value.Should().BeNull("外层 Dispose 恢复初始 null");
    }

    /// <summary>不同 AsyncLocal 实例的 tracker 互不影响(调用链独立)</summary>
    [Fact]
    public void DifferentAsyncLocals_Independent() {
        var al1 = new AsyncLocal<ImmutableDag<string>?>();
        var al2 = new AsyncLocal<ImmutableDag<string>?>();
        var tracker1 = new AskWaitGraphTracker(al1);
        var tracker2 = new AskWaitGraphTracker(al2);

        using var scope1 = tracker1.EnterScope("A", "B");
        al2.Value.Should().BeNull("tracker1 操作 al1,不影响 al2");

        using var scope2 = tracker2.EnterScope("C", "D");
        al1.Value!.Edges.Values.Should().ContainSingle(e => e.FromId == "A");
        al2.Value!.Edges.Values.Should().ContainSingle(e => e.FromId == "C");

        scope2!.Dispose();
        scope1!.Dispose();
    }

    /// <summary>链式调用 A→B→C 无环,各 scope 成功</summary>
    [Fact]
    public void ChainABC_NoCycle_AllSucceed() {
        var al = new AsyncLocal<ImmutableDag<string>?>();
        var tracker = new AskWaitGraphTracker(al);

        using var s1 = tracker.EnterScope("A", "B");
        using var s2 = tracker.EnterScope("B", "C");

        s1.Should().NotBeNull();
        s2.Should().NotBeNull("A→B→C 是链非环,加边成功");
        al.Value!.Edges.Values.Should().HaveCount(2);
        s2!.Dispose();
        s1!.Dispose();
    }

    /// <summary>链式 A→B→C 后加 C→A 形成环 → 抛异常</summary>
    [Fact]
    public void ChainABC_ThenCtoA_ThrowsCycle() {
        var al = new AsyncLocal<ImmutableDag<string>?>();
        var tracker = new AskWaitGraphTracker(al);

        using var s1 = tracker.EnterScope("A", "B");
        using var s2 = tracker.EnterScope("B", "C");

        Action act = () => tracker.EnterScope("C", "A");
        act.Should().Throw<ActorCyclicAskException>("A→B→C→A 形成间接环");

        s2!.Dispose();
        s1!.Dispose();
    }
}
