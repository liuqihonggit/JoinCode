namespace JoinCode.Composition.Improvement.Tests;

/// <summary>
/// G6 经验继承红测试 — 期望值来自 Harness² 论文（arXiv:2610.14224）：
/// ① 有效经验必需 cue/lesson/evidence 三字段（DSG048 §2.2.4 has_experience）；
/// ② 下个任务从公共基底 H₀ 重启，绝不自动继承成品（Code 73.4→63.2 回退红线）；
/// ③ 只通过 Propose 有选择复用，显式 adopt 才改变行为。
/// <para>纯逻辑测试，零磁盘 IO（JCC9001）— 持久化与两轮真机行为对比由 exe 验收覆盖。</para>
/// </summary>
public sealed class ImprovementTest {
    // ---------- 构造辅助 ----------

    private static ImprovementEntry Valid(string cue = "rate-limit") =>
        new(
            Cue: cue,
            Lesson: "调大限流阈值以承载压测",
            Evidence: "jcc tool tree 行为对比：并发 200 无 429",
            Patch: new HarnessPatch("chat-pipeline", "stream-rate-limit-mw", "max-requests", "50"));

    private static FrozenDictionary<string, NodeProfile> BaseProfiles() {
        var nodes = new[] {
            new NodeEntryConfig("stream-rate-limit-mw", 100, null,
                new Dictionary<string, string> { ["max-requests"] = "30", ["window-seconds"] = "60" }.ToFrozenDictionary()),
            new NodeEntryConfig("chat-timing-mw", 200, null, FrozenDictionary<string, string>.Empty),
        };
        var consumers = new Dictionary<string, ConsumerConfig> {
            ["chat-pipeline"] = new ConsumerConfig("chat-pipeline", nodes),
        }.ToFrozenDictionary();
        var baseProfile = new NodeProfile("base", null, consumers, FrozenSet<string>.Empty, [], FrozenDictionary<string, ConsumerConfig>.Empty);
        return new Dictionary<string, NodeProfile> { ["base"] = baseProfile }.ToFrozenDictionary();
    }

    private static string? ConfigOf(ResolvedProfile profile, string consumer, string node, string key) =>
        profile.Consumers[consumer].Nodes.First(n => n.Id == node).Config.GetValueOrDefault(key);

    // ---------- ① 三字段校验（has_experience） ----------

    [Fact]
    public void Validate_EmptyCue_Throws() {
        var act = () => ImprovementLibrary.Validate(new ImprovementEntry("", "lesson", "evidence"));
        act.Should().Throw<ArgumentException>().WithMessage("*cue*");
    }

    [Fact]
    public void Validate_EmptyLesson_Throws() {
        var act = () => ImprovementLibrary.Validate(new ImprovementEntry("cue", "  ", "evidence"));
        act.Should().Throw<ArgumentException>().WithMessage("*lesson*");
    }

    [Fact]
    public void Validate_EmptyEvidence_Throws() {
        var act = () => ImprovementLibrary.Validate(new ImprovementEntry("cue", "lesson", ""));
        act.Should().Throw<ArgumentException>().WithMessage("*evidence*");
    }

    [Fact]
    public void Validate_AllThreeFields_Accepted() {
        var act = () => ImprovementLibrary.Validate(Valid());
        act.Should().NotThrow();
    }

    // ---------- ② append-only ----------

    [Fact]
    public void Append_DoesNotMutateOriginal_AndAddsOne() {
        var original = new[] { Valid("a"), Valid("b") };

        var appended = ImprovementLibrary.Append(original, Valid("c"));

        appended.Should().HaveCount(3);
        original.Should().HaveCount(2, "append 必须返回新集合，不得就地改写历史（every run is traceable）");
        appended.Take(2).Should().Equal(new[] { original[0], original[1] }, "历史条目顺序与内容不变");
    }

    // ---------- ③ Propose 按 cue 选择性复用 ----------

    [Fact]
    public void Propose_ByCue_OnlyReturnsMatches() {
        var library = new[] { Valid("rate-limit"), Valid("ci"), Valid("rate-limit-overflow") };

        var proposed = ImprovementLibrary.Propose(library, "rate-limit");

        proposed.Should().HaveCount(2);
        proposed.Should().OnlyContain(e => e.Cue.Contains("rate-limit", StringComparison.Ordinal));
    }

    [Fact]
    public void Propose_CaseInsensitive_AndNoMatchReturnsEmpty() {
        var library = new[] { Valid("Rate-Limit") };

        ImprovementLibrary.Propose(library, "rate-limit").Should().HaveCount(1);
        ImprovementLibrary.Propose(library, "web").Should().BeEmpty();
    }

    [Fact]
    public void Propose_BoundaryMatch_NoSubstringFalsePositive() {
        var library = new[] { Valid("webhook"), Valid("web") };

        // 边界匹配：cue="web" 不得把 "webhook" 当命中（旧 Contains 实现会误命中）
        ImprovementLibrary.Propose(library, "web").Should().HaveCount(1);
        ImprovementLibrary.Propose(library, "web").Single().Cue.Should().Be("web");
    }

    [Fact]
    public void Propose_EmptyEntryCue_NeverMatches() {
        var library = new[] { Valid("") };

        ImprovementLibrary.Propose(library, "anything").Should().BeEmpty("空 cue 经验不得通杀所有任务");
    }

    [Fact]
    public void TryParseLine_InvalidEntry_Rejected() {
        ImprovementLibrary.TryParseLine("{\"cue\":\"\",\"lesson\":\"L\",\"evidence\":\"E\"}", out _, out var error).Should().BeFalse();
        error.Should().Contain("cue");
        ImprovementLibrary.TryParseLine("not-json", out _, out var jsonError).Should().BeFalse();
        jsonError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Propose_IsReadOnly_DoesNotChangeLibrary() {
        var library = new[] { Valid("rate-limit") };

        _ = ImprovementLibrary.Propose(library, "rate-limit");

        library.Should().HaveCount(1, "Propose 只返回候选，绝不自动写入或应用");
    }

    // ---------- ④ H₀ 重置红线（论文实证） ----------

    [Fact]
    public void Compose_NoPatches_AlwaysReturnsH0_EvenAfterPriorRoundAdopted() {
        var profiles = BaseProfiles();
        var patch = Valid().Patch;
        patch.Should().NotBeNull();
        var adopted = patch ?? throw new InvalidOperationException("测试前置失败：经验条目必须携带 patch");

        // 第一轮：显式 adopt，profile 被改变（Patch 生效 → 非死数据）
        var round1 = HarnessComposer.Compose(profiles, "base", [adopted]);
        ConfigOf(round1, "chat-pipeline", "stream-rate-limit-mw", "max-requests").Should().Be("50");

        // 第二轮：不 adopt —— 必须从 H₀ 重新解析，绝不继承第一轮的成品
        var round2 = HarnessComposer.Compose(profiles, "base", []);

        ConfigOf(round2, "chat-pipeline", "stream-rate-limit-mw", "max-requests").Should().Be("30",
            "H₀ 重置红线：第二轮初始状态必须等于 base 基底，而非上一轮 adopt 后的成品");
        round2.Should().NotBeEquivalentTo(round1);
    }

    [Fact]
    public void Compose_ProposeAlone_DoesNotLeakIntoNextRound() {
        var profiles = BaseProfiles();
        var library = new[] { Valid() };

        // Propose 命中经验，但未 adopt
        var candidates = ImprovementLibrary.Propose(library, "rate-limit");
        candidates.Should().HaveCount(1);

        var round = HarnessComposer.Compose(profiles, "base", []);
        ConfigOf(round, "chat-pipeline", "stream-rate-limit-mw", "max-requests").Should().Be("30",
            "仅 Propose 命中不得改变 harness，必须显式 adopt");
    }

    [Fact]
    public void Compose_WithPatch_ChangesOnlyTargetNode() {
        var profiles = BaseProfiles();
        var patch = new HarnessPatch("chat-pipeline", "stream-rate-limit-mw", "max-requests", "99");

        var round = HarnessComposer.Compose(profiles, "base", [patch]);

        ConfigOf(round, "chat-pipeline", "stream-rate-limit-mw", "max-requests").Should().Be("99");
        ConfigOf(round, "chat-pipeline", "stream-rate-limit-mw", "window-seconds").Should().Be("60",
            "patch 只覆写目标键，未提及的配置从 H₀ 保留");
        round.Consumers["chat-pipeline"].Nodes.First(n => n.Id == "chat-timing-mw").Priority.Should().Be(200,
            "非目标节点完全不受影响");
    }

    [Fact]
    public void Compose_PatchTargetMissing_ThrowsWithGuidance() {
        var profiles = BaseProfiles();
        var patch = new HarnessPatch("chat-pipeline", "nonexistent-mw", "key", "value");

        var act = () => HarnessComposer.Compose(profiles, "base", [patch]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*nonexistent-mw*");
    }

    [Fact]
    public void ComposeSafe_IncompatiblePatch_FallsBackToCleanProfile_NoThrow() {
        var profiles = BaseProfiles();
        var patch = new HarnessPatch("chat-pipeline", "nonexistent-mw", "key", "value");

        var act = () => HarnessComposer.ComposeSafe(profiles, "base", [patch]);

        act.Should().NotThrow("不当经验绝不拖垮宿主启动");
        var clean = act();
        ConfigOf(clean, "chat-pipeline", "stream-rate-limit-mw", "max-requests").Should().Be("30",
            "回退到干净 H₀，不采纳任何不兼容 patch");
    }
}
