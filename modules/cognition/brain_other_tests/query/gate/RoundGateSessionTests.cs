namespace Core.Tests.Query.Gate;

/// <summary>
/// TASK132 轮次闸门状态机测试 — 计数熔断、推荐/申诉、验证强制边与完成终验。
/// </summary>
public sealed class RoundGateSessionTests {
    private static RoundGateInvocation Invoke(
        RoundGateChoice choice, RoundGateVerdict verdict = RoundGateVerdict.None,
        string? evidence = null, string? subject = null) => new() {
        Choice = choice,
        Verdict = verdict,
        Evidence = evidence,
        Subject = subject,
    };

    /// <summary>取熔断强制项 — 未触发时直接失败（避免 null-forgiving）。</summary>
    private static RoundGateFuse RequireFuse(RoundGateSession session) =>
        session.Fuse ?? throw new InvalidOperationException("预期触发熔断，但当前没有熔断项");

    [Fact]
    public void EvaluateStop_WithoutGateCall_IsBlocked() {
        var session = new RoundGateSession(strictMode: false);

        var verdict = session.EvaluateStop(required: true);

        verdict.Allowed.Should().BeFalse();
        verdict.Reason.Should().Contain(RoundGatePolicy.ToolName);
    }

    [Fact]
    public void EvaluateStop_DisabledGate_AlwaysAllows() {
        var session = new RoundGateSession(strictMode: true);

        session.EvaluateStop(required: false).Allowed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateStop_MiddleStateOption_CannotStop() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Continue, evidence: "dotnet build 成功"));

        var verdict = session.EvaluateStop(required: true);

        verdict.Allowed.Should().BeFalse();
        verdict.Reason.Should().Contain("中间态");
    }

    [Fact]
    public void EvaluateStop_DoneWithoutSelfAccept_IsBlocked() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Continue, evidence: "文件已改"));
        session.Record(Invoke(RoundGateChoice.Done, evidence: "改完了"));

        var verdict = session.EvaluateStop(required: true);

        verdict.Allowed.Should().BeFalse("产出必须先过 3 自验才能碰 5 完成");
        verdict.Reason.Should().Contain("自验");
    }

    [Fact]
    public void EvaluateStop_SelfAcceptPassThenDone_AllowsStop() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Continue, evidence: "dotnet build 成功"));
        session.Record(Invoke(RoundGateChoice.SelfAccept, RoundGateVerdict.Pass, evidence: "dotnet test 全绿"));
        session.Record(Invoke(RoundGateChoice.Done, evidence: "验收表逐条勾选"));

        var verdict = session.EvaluateStop(required: true);

        verdict.Allowed.Should().BeTrue();
        session.SelfAcceptPassed.Should().BeTrue();
    }

    [Fact]
    public void EvaluateStop_NewExecutionInvalidatesPriorSelfAccept() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.SelfAccept, RoundGateVerdict.Pass));
        session.Record(Invoke(RoundGateChoice.Continue, evidence: "继续改代码"));
        session.Record(Invoke(RoundGateChoice.Done));

        session.EvaluateStop(required: true).Allowed.Should().BeFalse();
    }

    [Fact]
    public void StrictMode_DoneRequiresUserAcceptanceHandoff() {
        var session = new RoundGateSession(strictMode: true);
        session.Record(Invoke(RoundGateChoice.SelfAccept, RoundGateVerdict.Pass, evidence: "自验通过"));
        session.Record(Invoke(RoundGateChoice.Done));

        var blocked = session.EvaluateStop(required: true);
        blocked.Allowed.Should().BeFalse();
        blocked.Reason.Should().Contain("用户验收");

        session.Record(Invoke(RoundGateChoice.UserAccept, evidence: "○ 清单已列"));
        session.Record(Invoke(RoundGateChoice.Done));
        session.EvaluateStop(required: true).Allowed.Should().BeTrue();
    }

    [Fact]
    public void DegradeDelivery_RequiresUserAcceptanceHandoff() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Degrade, evidence: "可用子集已交付"));

        session.EvaluateStop(required: true).Allowed.Should().BeFalse("降级必须先移交用户验收");

        session.Record(Invoke(RoundGateChoice.UserAccept, evidence: "降级方案已列"));
        session.Record(Invoke(RoundGateChoice.Degrade));
        session.EvaluateStop(required: true).Allowed.Should().BeTrue();
    }

    [Fact]
    public void ExitChoices_BlockedEscalateAwaitUserAccept_AllowStop() {
        var session = new RoundGateSession(strictMode: true);

        session.Record(Invoke(RoundGateChoice.BlockedReport));
        session.EvaluateStop(required: true).Allowed.Should().BeTrue("缺用户输入是合法出口");

        session.Record(Invoke(RoundGateChoice.Escalate, evidence: "已尝试 3 种方案"));
        session.EvaluateStop(required: true).Allowed.Should().BeTrue("求助/升级是合法收敛出口");

        session.Record(Invoke(RoundGateChoice.AwaitBackground, evidence: "job id=42"));
        session.EvaluateStop(required: true).Allowed.Should().BeTrue("等候后台是合法挂起出口");
    }

    [Fact]
    public void Recommend_PrefersSelfAcceptAfterExecution() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Continue, evidence: "已改文件"));

        var recommendation = session.Recommend();

        recommendation.Choice.Should().Be(RoundGateChoice.SelfAccept);
        recommendation.Alternatives.Should().Contain(RoundGateChoice.Rework);
    }

    [Fact]
    public void Recommend_AfterPassedSelfAccept_PrefersDone() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.SelfAccept, RoundGateVerdict.Pass));

        session.Recommend().Choice.Should().Be(RoundGateChoice.Done);
    }

    [Fact]
    public void Fuse_IdleWithoutEvidence_ForcesReplan() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Continue));

        var step = session.Record(Invoke(RoundGateChoice.Continue));

        step.Fuse.Should().NotBeNull();
        RequireFuse(session).ForcedChoice.Should().Be(RoundGateChoice.Replan);
        session.Recommend().Choice.Should().Be(RoundGateChoice.Replan);
    }

    [Fact]
    public void Fuse_EvidenceResetsIdleStreak() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Continue));
        session.Record(Invoke(RoundGateChoice.Continue, evidence: "测试输出通过"));

        session.IdleStreak.Should().Be(0);
        session.Fuse.Should().BeNull();
    }

    [Fact]
    public void Fuse_RepeatedSelfAcceptFailures_ForceEscalate() {
        var session = new RoundGateSession(strictMode: false);

        for (var i = 0; i < RoundGateSession.MaxSelfAcceptFailStreak; i++)
            session.Record(Invoke(RoundGateChoice.SelfAccept, RoundGateVerdict.Fail, evidence: $"失败{i}"));

        session.Fuse.Should().NotBeNull();
        RequireFuse(session).ForcedChoice.Should().Be(RoundGateChoice.Escalate);
    }

    [Fact]
    public void Fuse_SameDefectReworkedTooOften_ForcesReplan() {
        var session = new RoundGateSession(strictMode: false);

        for (var i = 0; i <= RoundGateSession.MaxDefectReworks; i++)
            session.Record(Invoke(RoundGateChoice.Rework, evidence: $"返工{i}", subject: "缺陷A"));

        session.DefectReworkCount("缺陷A").Should().Be(RoundGateSession.MaxDefectReworks + 1);
        session.Fuse.Should().NotBeNull();
        RequireFuse(session).ForcedChoice.Should().Be(RoundGateChoice.Replan);
    }

    [Fact]
    public void Fuse_ReplanTooOften_ForcesEscalate() {
        var session = new RoundGateSession(strictMode: false);

        for (var i = 0; i <= RoundGateSession.MaxReplans; i++)
            session.Record(Invoke(RoundGateChoice.Replan, evidence: $"新计划{i}"));

        session.ReplanCount.Should().Be(RoundGateSession.MaxReplans + 1);
        session.Fuse.Should().NotBeNull();
        RequireFuse(session).ForcedChoice.Should().Be(RoundGateChoice.Escalate);
    }

    [Fact]
    public void Fuse_UserAcceptanceRejectedTwice_ForcesReplan() {
        var session = new RoundGateSession(strictMode: true);

        for (var i = 0; i < RoundGateSession.MaxUserRejectStreak; i++) {
            session.Record(Invoke(RoundGateChoice.UserAccept, evidence: "移交"));
            session.Record(Invoke(RoundGateChoice.Rework, evidence: "用户驳回", subject: "缺陷B"));
        }

        session.UserRejectStreak.Should().Be(RoundGateSession.MaxUserRejectStreak);
        session.Fuse.Should().NotBeNull();
        RequireFuse(session).ForcedChoice.Should().Be(RoundGateChoice.Replan);
    }

    [Fact]
    public void Fuse_BlocksExitUntilForcedChoiceSelected() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Continue));
        session.Record(Invoke(RoundGateChoice.Continue));
        session.Record(Invoke(RoundGateChoice.Escalate, evidence: "强行求助"));

        var verdict = session.EvaluateStop(required: true);

        verdict.Allowed.Should().BeFalse("熔断要求改选 7 重新规划");
        verdict.Reason.Should().Contain("熔断");
    }

    [Fact]
    public void Appeal_DeviatingFromRecommendation_IsRecordedAndLocks() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Continue, evidence: "已改文件"));

        var first = session.Record(Invoke(RoundGateChoice.Rework, evidence: "返工", subject: "缺陷C"));
        first.IsAppeal.Should().BeTrue();
        first.AppealLocked.Should().BeFalse();

        session.Record(Invoke(RoundGateChoice.Continue, evidence: "继续"));
        var second = session.Record(Invoke(RoundGateChoice.Replan, evidence: "重规划"));

        second.IsAppeal.Should().BeTrue();
        second.AppealLocked.Should().BeTrue("连续申诉 2 次后闸门锁定推荐项");
    }

    [Fact]
    public void Record_SelfAcceptPass_RecommendsDoneForNextRound() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Continue, evidence: "已改文件"));

        var step = session.Record(Invoke(RoundGateChoice.SelfAccept, verdict: RoundGateVerdict.Pass, evidence: "测试全绿"));

        step.Recommendation.Choice.Should().Be(
            RoundGateChoice.Done, "推荐必须反映本次选择之后的状态，否则注入给 AI 的'下一轮推荐'会滞后一格");
    }

    [Fact]
    public void StateAdvisory_ReportsRecommendationAppealAndFuse() {
        var session = new RoundGateSession(strictMode: false);
        session.Record(Invoke(RoundGateChoice.Continue));
        var step = session.Record(Invoke(RoundGateChoice.Replan, evidence: "重规划"));

        var text = RoundGatePrompts.BuildStateAdvisory(step);

        text.Should().Contain("推荐下一轮");
        text.Should().Contain("偏离推荐");
        text.Should().Contain("备选");
    }
}
