namespace JoinCode.Composition.Improvement.Tests;

/// <summary>
/// TASK098 融合回归网 — 统一 Delta 模型 + 可回滚句柄 + Probe 行为门禁。
/// <para>真机 exe 验收是唯一标准（jcc probe / --dump-nodes 自检），本测试只防未来回归。</para>
/// </summary>
public sealed class FusionDeltaTest {
    private static ResolvedProfile Skeleton() {
        var nodes = new[] {
            new NodeEntryConfig("stream-rate-limit-mw", 100, null,
                new Dictionary<string, string> { ["max-requests"] = "30", ["window-seconds"] = "60" }.ToFrozenDictionary()),
        };
        var consumers = new Dictionary<string, ConsumerConfig> {
            ["chat-pipeline"] = new ConsumerConfig("chat-pipeline", nodes),
        }.ToFrozenDictionary();
        var profile = new NodeProfile("base", null, consumers, FrozenSet<string>.Empty, [], FrozenDictionary<string, ConsumerConfig>.Empty);
        return new JoinCode.Composition.NodeRegistry.NodeProfileResolver(
            new Dictionary<string, NodeProfile> { ["base"] = profile }.ToFrozenDictionary()).Resolve("base");
    }

    private static string? MaxOf(ResolvedProfile p) =>
        p.Consumers["chat-pipeline"].Nodes.First(n => n.Id == "stream-rate-limit-mw").Config.GetValueOrDefault("max-requests");

    [Fact]
    public void Compose_PermanentDelta_BecomesPartOfH0() {
        var sk = Skeleton();
        var permanent = CompositionDelta.Permanent("chat-pipeline", "stream-rate-limit-mw", "max-requests", "40");
        var handle = DeltaComposer.Compose(sk, [permanent]);

        MaxOf(handle.H0).Should().Be("40");
        MaxOf(handle.Current).Should().Be("40");
        handle.Dispose();
        MaxOf(handle.Current).Should().Be("40", "Permanent 不随 Dispose 回滚");
    }

    [Fact]
    public void Compose_RoundDelta_OnlyInCurrent_AndDisposeRestoresH0() {
        var sk = Skeleton();
        var round = CompositionDelta.FromExperience(
            new HarnessPatch("chat-pipeline", "stream-rate-limit-mw", "max-requests", "50"), "rate-limit", "evidence");
        var handle = DeltaComposer.Compose(sk, [round]);

        MaxOf(handle.H0).Should().Be("30", "H₀ 永不混入 Round");
        MaxOf(handle.Current).Should().Be("50");
        handle.AdoptedRoundCount.Should().Be(1);
        handle.Dispose();
        MaxOf(handle.Current).Should().Be("30", "Dispose 即 G6 H₀ 重置");
        handle.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Handle_TracksRegistrations_AndDisposesLifo() {
        var sk = Skeleton();
        var order = new List<string>();
        var handle = DeltaComposer.Compose(sk, []);
        handle.Track(new ActionDisposable(() => order.Add("first")));
        handle.Track(new ActionDisposable(() => order.Add("second")));
        handle.Dispose();

        order.Should().Equal("second", "first");
    }

    [Fact]
    public void Handle_TrackAfterDispose_ImmediatelyRollsBackAndThrows() {
        var handle = DeltaComposer.Compose(Skeleton(), []);
        handle.Dispose();
        var rolledBack = false;

        var act = () => handle.Track(new ActionDisposable(() => rolledBack = true));
        act.Should().Throw<ObjectDisposedException>();
        rolledBack.Should().BeTrue("已 Dispose 的句柄不能泄漏新登记的资源");
    }

    [Fact]
    public void ComposeSafe_BadDelta_FallsBackToH0() {
        var bad = CompositionDelta.FromExperience(
            new HarnessPatch("missing-consumer", "stream-rate-limit-mw", "max-requests", "50"), "bad", "evidence");
        using var handle = DeltaComposer.ComposeSafe(Skeleton(), [bad]);

        MaxOf(handle.Current).Should().Be("30", "不兼容 delta fail-closed 回干净态");
        handle.AdoptedRoundCount.Should().Be(0);
    }

    [Fact]
    public void BehaviorProbe_PositivePatch_Passes() {
        var sk = Skeleton();
        var round = CompositionDelta.FromExperience(
            new HarnessPatch("chat-pipeline", "stream-rate-limit-mw", "max-requests", "50"), "rate-limit", "evidence");
        using var handle = DeltaComposer.Compose(sk, [round]);

        var verdict = BehaviorProbe.Compare(sk, handle.Current);
        verdict.Passed.Should().BeTrue();
        verdict.Baseline.Allowed.Should().Be(30);
        verdict.Candidate.Allowed.Should().Be(50);
        verdict.Baseline.Denied.Should().Be(30, "60 请求 - 30 允许");
    }

    [Fact]
    public void BehaviorProbe_NoOpPatch_Fails() {
        var sk = Skeleton();
        var round = CompositionDelta.FromExperience(
            new HarnessPatch("chat-pipeline", "stream-rate-limit-mw", "max-requests", "30"), "noop", "evidence");
        using var handle = DeltaComposer.Compose(sk, [round]);

        var verdict = BehaviorProbe.Compare(sk, handle.Current);
        verdict.Passed.Should().BeFalse("零行为差异 = 死经验");
        verdict.Reason.Should().Contain("行为零差异");
    }

    [Fact]
    public void BehaviorProbe_ReversePatch_Fails() {
        var sk = Skeleton();
        var round = CompositionDelta.FromExperience(
            new HarnessPatch("chat-pipeline", "stream-rate-limit-mw", "max-requests", "20"), "reverse", "evidence");
        using var handle = DeltaComposer.Compose(sk, [round]);

        var verdict = BehaviorProbe.Compare(sk, handle.Current);
        verdict.Passed.Should().BeFalse("行为反向 = 把系统改紧");
        verdict.Reason.Should().Contain("行为反向");
    }

    private sealed class ActionDisposable(Action action) : IDisposable {
        public void Dispose() => action();
    }
}
