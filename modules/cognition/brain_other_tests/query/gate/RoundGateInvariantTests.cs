namespace Core.Tests.Query.Gate;

/// <summary>
/// TASK132 验收 — 状态机不变量穷举：任意 ≤4 步选择序列（含证据/无证据、pass/fail/未声明、严格/非严格）
/// 都必须满足同一组从需求独立推出的不变量，而不是只验证我手写的几条样例。
/// </summary>
public sealed class RoundGateInvariantTests {
    private sealed record Step(RoundGateChoice Choice, RoundGateVerdict Verdict, string? Evidence) {
        public RoundGateInvocation ToInvocation() => new() { Choice = Choice, Verdict = Verdict, Evidence = Evidence };

        public override string ToString() =>
            $"{RoundGatePolicy.Describe(Choice)}|{Verdict.ToValue()}|{(Evidence is null ? "无证据" : "有证据")}";
    }

    private static readonly IReadOnlyList<Step> Steps = BuildSteps();

    private static IReadOnlyList<Step> BuildSteps() {
        var steps = new List<Step>();
        foreach (var choice in Enum.GetValues<RoundGateChoice>()) {
            foreach (var evidence in new string?[] { null, "有产出" }) {
                if (choice == RoundGateChoice.SelfAccept) {
                    foreach (var verdict in Enum.GetValues<RoundGateVerdict>())
                        steps.Add(new Step(choice, verdict, evidence));
                } else {
                    steps.Add(new Step(choice, RoundGateVerdict.None, evidence));
                }
            }
        }

        return steps;
    }

    /// <summary>穷举深度 1~4 的全部序列 × 严格/非严格，共约 95 万个状态机实例。</summary>
    [Fact]
    public void AnySequenceUpToFourSteps_HoldsInvariants() {
        for (var depth = 1; depth <= 4; depth++) {
            foreach (var strict in new[] { false, true }) {
                var buffer = new int[depth];
                ForEachSequence(buffer, 0, strict);
            }
        }
    }

    /// <summary>关闭闸门时任何序列都必须放行 — 开关语义独立于状态机。</summary>
    [Fact]
    public void DisabledGate_NeverBlocks() {
        var session = new RoundGateSession(strictMode: true);
        session.Record(new Step(RoundGateChoice.Replan, RoundGateVerdict.None, null).ToInvocation());

        session.EvaluateStop(required: false).Allowed.Should().BeTrue();
    }

    private static void ForEachSequence(int[] buffer, int index, bool strict) {
        if (index == buffer.Length) {
            Check(buffer, strict);
            return;
        }

        for (var i = 0; i < Steps.Count; i++) {
            buffer[index] = i;
            ForEachSequence(buffer, index + 1, strict);
        }
    }

    private static void Check(int[] indexes, bool strict) {
        var session = new RoundGateSession(strict);
        var sequence = new List<Step>(indexes.Length);
        foreach (var stepIndex in indexes) {
            var step = Steps[stepIndex];
            sequence.Add(step);
            session.Record(step.ToInvocation());
        }

        var last = sequence[^1].Choice;
        var verdict = session.EvaluateStop(required: true);

        // I1 中间态永不放行
        if (!RoundGatePolicy.IsExitChoice(last) && verdict.Allowed)
            Fail("中间态被放行", sequence, strict, session);

        // I2 5 完成 ⇒ 最近一次"执行/元操作作废边"之后有 3+pass
        if (last == RoundGateChoice.Done && verdict.Allowed && !PassedSelfAcceptAfterLastInvalidation(sequence))
            Fail("5 完成放行但没有有效的 3+pass", sequence, strict, session);

        // I3 熔断不可绕过：最后选择未改选强制项时必须拦截
        if (verdict.Allowed && session.Fuse is { } fuse && last != fuse.ForcedChoice)
            Fail($"熔断强制 {(int)fuse.ForcedChoice} 却被 {(int)last} 绕过", sequence, strict, session);

        // I4 9 降级 ⇒ 最近一次"作废/移交"动作是 4 用户验收
        if (last == RoundGateChoice.Degrade && verdict.Allowed && LastHandoffMarker(sequence) != RoundGateChoice.UserAccept)
            Fail("9 降级放行但没有已移交的 4 用户验收", sequence, strict, session);

        // I5 严格模式 5 完成 ⇒ 同上，必须有已移交的 4
        if (strict && last == RoundGateChoice.Done && verdict.Allowed && LastHandoffMarker(sequence) != RoundGateChoice.UserAccept)
            Fail("严格模式 5 完成放行但没有已移交的 4 用户验收", sequence, strict, session);

        // I8 11 放弃 ⇒ 序列里必须已有 3 verdict=fail
        if (last == RoundGateChoice.Abandon && verdict.Allowed && !sequence.Any(
                s => s.Choice == RoundGateChoice.SelfAccept && s.Verdict == RoundGateVerdict.Fail))
            Fail("11 放弃放行但没有失败记录", sequence, strict, session);

        // I9 严格模式 11 放弃 ⇒ 必须有已移交的 4
        if (strict && last == RoundGateChoice.Abandon && verdict.Allowed
            && LastHandoffMarker(sequence) != RoundGateChoice.UserAccept)
            Fail("严格模式 11 放弃放行但没有已移交的 4 用户验收", sequence, strict, session);

        // I6 计数单源：ReplanCount 等于序列中 7 的出现次数
        var replans = sequence.Count(s => s.Choice == RoundGateChoice.Replan);
        if (session.ReplanCount != replans)
            Fail($"ReplanCount={session.ReplanCount} 与实际 {replans} 不符", sequence, strict, session);

        // I10 失败依据单源：FailedSelfAccepts 等于序列中 3+fail 的次数
        var failures = sequence.Count(s => s.Choice == RoundGateChoice.SelfAccept && s.Verdict == RoundGateVerdict.Fail);
        if (session.FailedSelfAccepts != failures)
            Fail($"FailedSelfAccepts={session.FailedSelfAccepts} 与实际 {failures} 不符", sequence, strict, session);

        // I7 自验状态与序列一致：最近一次 3 为 pass 且之后无作废动作 ⇒ 通过，否则未通过
        if (session.SelfAcceptPassed != ExpectedSelfAcceptPassed(sequence))
            Fail($"SelfAcceptPassed={session.SelfAcceptPassed} 与序列推断不一致", sequence, strict, session);
    }

    /// <summary>作废自验的动作：1/2 执行推进、7 重规划、8 回退。</summary>
    private static bool IsInvalidating(RoundGateChoice choice) => choice is
        RoundGateChoice.Continue or RoundGateChoice.NextTodo or RoundGateChoice.Replan or RoundGateChoice.Rework;

    private static bool PassedSelfAcceptAfterLastInvalidation(List<Step> sequence) {
        var start = sequence.FindLastIndex(s => IsInvalidating(s.Choice));
        return sequence.Skip(start + 1)
            .Any(s => s.Choice == RoundGateChoice.SelfAccept && s.Verdict == RoundGateVerdict.Pass);
    }

    /// <summary>最近的"移交/作废"动作 —— 4 移交、1/2/7/8 作废，取最后出现的那个。</summary>
    private static RoundGateChoice LastHandoffMarker(List<Step> sequence) {
        var marker = sequence.FindLast(s => s.Choice is
            RoundGateChoice.UserAccept or RoundGateChoice.Continue or RoundGateChoice.NextTodo
            or RoundGateChoice.Replan or RoundGateChoice.Rework);
        return marker?.Choice ?? RoundGateChoice.BlockedReport;
    }

    private static bool ExpectedSelfAcceptPassed(List<Step> sequence) {
        var lastSelfAccept = sequence.FindLastIndex(s => s.Choice == RoundGateChoice.SelfAccept);
        if (lastSelfAccept < 0)
            return false;

        var lastInvalidation = sequence.FindLastIndex(s => IsInvalidating(s.Choice));
        if (lastInvalidation > lastSelfAccept)
            return false;

        return sequence[lastSelfAccept].Verdict == RoundGateVerdict.Pass;
    }

    private static void Fail(string invariant, List<Step> sequence, bool strict, RoundGateSession session) =>
        throw new InvalidOperationException(
            $"不变量失败 [{invariant}] strict={strict} SelfAcceptPassed={session.SelfAcceptPassed} " +
            $"Replan={session.ReplanCount} 序列={string.Join(" → ", sequence)}");
}
