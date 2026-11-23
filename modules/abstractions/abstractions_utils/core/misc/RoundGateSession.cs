namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 轮次闸门状态机 — 每轮强制三问 + 状态流转 + 计数熔断 + 推荐/申诉 + 完成终验。
/// <para>
/// 单次查询（一次 <c>QueryAsync</c>）持有一个实例；计数器随本轮任务的生命周期累加，
/// 是否允许收尾一律由 <see cref="EvaluateStop"/> 裁决，禁止在循环里另写一套判定。
/// </para>
/// <para>
/// 设计要点（对应用户提案）：
/// ① 产出必须过验证（3 自验）才能碰完成（5）；② 补往回走（8/7）与降级/求助（9/10）的合法出口；
/// ③ 用计数熔断 + 推荐/申诉替代平铺选项与重复确认。
/// </para>
/// </summary>
public sealed class RoundGateSession {
    /// <summary>同一任务最多重规划次数 — 超过强制转 10 求助/升级。</summary>
    public const int MaxReplans = 2;

    /// <summary>同一缺陷最多返工次数 — 超过强制转 7 重新规划（怀疑方案本身有问题）。</summary>
    public const int MaxDefectReworks = 2;

    /// <summary>连续自验不通过次数上限 — 达到强制转 10 求助/升级。</summary>
    public const int MaxSelfAcceptFailStreak = 3;

    /// <summary>连续空转（无证据推进）轮次上限 — 达到强制转 7 重新规划。</summary>
    public const int MaxIdleStreak = 2;

    /// <summary>用户验收连续被驳回次数上限 — 达到强制转 7 重新规划。</summary>
    public const int MaxUserRejectStreak = 2;

    /// <summary>连续申诉（实选偏离推荐）次数上限 — 达到后闸门锁定推荐项。</summary>
    public const int MaxAppealStreak = 2;

    /// <summary>未提供缺陷标识时使用的默认键。</summary>
    public const string DefaultSubject = "__default__";

    private readonly Dictionary<string, int> _defectReworks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 创建闸门会话。
    /// </summary>
    /// <param name="strictMode">严格模式 — 完成/降级前必须已移交用户验收（JCC_ROUND_GATE_STRICT）。</param>
    public RoundGateSession(bool strictMode) => StrictMode = strictMode;

    /// <summary>是否严格模式。</summary>
    public bool StrictMode { get; }

    /// <summary>上一次闸门选项（本轮尚未调用闸门时为 null）。</summary>
    public RoundGateChoice? LastChoice { get; private set; }

    /// <summary>本会话是否已获得通过的自验（3 verdict=pass）。</summary>
    public bool SelfAcceptPassed { get; private set; }

    /// <summary>本会话是否已把验收清单移交用户（4 用户验收）。</summary>
    public bool UserAcceptanceHandedOff { get; private set; }

    /// <summary>已重规划次数。</summary>
    public int ReplanCount { get; private set; }

    /// <summary>连续自验不通过次数。</summary>
    public int SelfAcceptFailStreak { get; private set; }

    /// <summary>连续空转（无证据推进）轮次。</summary>
    public int IdleStreak { get; private set; }

    /// <summary>用户验收连续被驳回（4 之后紧跟 8）次数。</summary>
    public int UserRejectStreak { get; private set; }

    /// <summary>连续申诉（实选偏离推荐）次数。</summary>
    public int AppealStreak { get; private set; }

    /// <summary>当前生效的熔断强制项（无熔断时为 null）。</summary>
    public RoundGateFuse? Fuse { get; private set; }

    /// <summary>
    /// 读取某个缺陷的累计返工次数。
    /// </summary>
    /// <param name="subject">缺陷/子项标识；null 使用默认键。</param>
    /// <returns>累计返工次数。</returns>
    public int DefectReworkCount(string? subject) =>
        _defectReworks.TryGetValue(NormalizeSubject(subject), out var count) ? count : 0;

    /// <summary>
    /// 计算下一轮推荐选项（按状态给 1 个默认 + 2 个备选，不平铺全部选项）。
    /// </summary>
    /// <returns>推荐结果。</returns>
    public RoundGateRecommendation Recommend() {
        if (IdleStreak >= MaxIdleStreak)
            return Recommend(RoundGateChoice.Replan, $"连续 {IdleStreak} 轮无证据推进（空转）——强制重规划",
                RoundGateChoice.Escalate, RoundGateChoice.Continue);

        if (SelfAcceptFailStreak >= MaxSelfAcceptFailStreak)
            return Recommend(RoundGateChoice.Escalate, $"连续 {SelfAcceptFailStreak} 轮自验不通过——强制求助/升级",
                RoundGateChoice.Replan, RoundGateChoice.Rework);

        if (UserRejectStreak >= MaxUserRejectStreak)
            return Recommend(RoundGateChoice.Replan, $"用户验收连续 {UserRejectStreak} 次被驳回——强制重规划",
                RoundGateChoice.Escalate, RoundGateChoice.Rework);

        if (ReplanCount > MaxReplans)
            return Recommend(RoundGateChoice.Escalate, $"已重规划 {ReplanCount} 次（上限 {MaxReplans}）——强制求助/升级",
                RoundGateChoice.Replan, RoundGateChoice.Continue);

        return LastChoice switch {
            null => Recommend(RoundGateChoice.Continue, "任务刚开始——直接推进", RoundGateChoice.NextTodo, RoundGateChoice.Replan),
            RoundGateChoice.Continue or RoundGateChoice.NextTodo or RoundGateChoice.Rework =>
                Recommend(RoundGateChoice.SelfAccept, "本轮已有产出——先做自我阶段性验收", RoundGateChoice.Continue, RoundGateChoice.Rework),
            RoundGateChoice.SelfAccept when SelfAcceptPassed =>
                StrictMode && !UserAcceptanceHandedOff
                    ? Recommend(RoundGateChoice.UserAccept, "严格模式：完成前必须移交用户验收", RoundGateChoice.Done, RoundGateChoice.Rework)
                    : Recommend(RoundGateChoice.Done, "自验通过——可进入完成终验", RoundGateChoice.UserAccept, RoundGateChoice.Rework),
            RoundGateChoice.SelfAccept =>
                Recommend(RoundGateChoice.Rework, "自验未通过——先返工修正缺陷", RoundGateChoice.Replan, RoundGateChoice.UserAccept),
            RoundGateChoice.UserAccept =>
                Recommend(RoundGateChoice.Done, "用户验收已移交——进入完成终验", RoundGateChoice.SelfAccept, RoundGateChoice.Rework),
            RoundGateChoice.Replan =>
                Recommend(RoundGateChoice.Continue, "新计划已就绪——执行第一步", RoundGateChoice.NextTodo, RoundGateChoice.Escalate),
            RoundGateChoice.AwaitBackground =>
                Recommend(RoundGateChoice.SelfAccept, "后台信号到达——先验证产出", RoundGateChoice.Continue, RoundGateChoice.Degrade),
            RoundGateChoice.Degrade =>
                Recommend(RoundGateChoice.UserAccept, "降级交付需用户验收确认", RoundGateChoice.Escalate, RoundGateChoice.Replan),
            _ => Recommend(RoundGateChoice.Continue, "上一轮已结束——新回合从头推进", RoundGateChoice.NextTodo, RoundGateChoice.Replan),
        };
    }

    /// <summary>
    /// 记录一次闸门选择 — 更新计数器、熔断与推荐偏差（申诉）。
    /// </summary>
    /// <param name="invocation">闸门调用语义。</param>
    /// <returns>本轮闸门状态快照（含推荐、申诉、熔断）。</returns>
    public RoundGateStep Record(RoundGateInvocation invocation) {
        ArgumentNullException.ThrowIfNull(invocation);

        var recommendation = Recommend();
        var isAppeal = invocation.Choice != recommendation.Choice;
        AppealStreak = isAppeal ? AppealStreak + 1 : 0;

        var hasEvidence = !string.IsNullOrWhiteSpace(invocation.Evidence);
        if (hasEvidence)
            IdleStreak = 0;
        else if (RoundGatePolicy.CategoryOf(invocation.Choice) is RoundGateCategory.Execute or RoundGateCategory.Meta)
            IdleStreak++;

        ApplyTransition(invocation);
        LastChoice = invocation.Choice;
        Fuse = ResolveFuse(invocation);

        return new RoundGateStep {
            Choice = invocation.Choice,
            Recommendation = recommendation,
            IsAppeal = isAppeal,
            AppealLocked = AppealStreak >= MaxAppealStreak,
            Fuse = Fuse,
        };
    }

    /// <summary>
    /// 收尾裁决 — 未过验证链/熔断未处理/非出口选项一律拦截，并给出可执行的原因与改选指引。
    /// </summary>
    /// <param name="required">本查询是否启用闸门。</param>
    /// <returns>裁决结果。</returns>
    public RoundGateStopVerdict EvaluateStop(bool required) {
        if (!required)
            return RoundGateStopVerdict.Allow;

        if (LastChoice is not { } recorded)
            return RoundGateStopVerdict.Block($"你还没有调用 {RoundGatePolicy.ToolName} 选择下一步。");

        if (!RoundGatePolicy.IsExitChoice(recorded))
            return RoundGateStopVerdict.Block(
                $"{RoundGatePolicy.Describe(recorded)} 是中间态，不允许收尾；产出必须先过 3 自验，完成只能选 5 完成。");

        if (Fuse is { } fuse && recorded != fuse.ForcedChoice)
            return RoundGateStopVerdict.Block($"熔断生效：{fuse.Reason}；必须改选 {RoundGatePolicy.Describe(fuse.ForcedChoice)}。");

        if (recorded == RoundGateChoice.Done) {
            if (!SelfAcceptPassed)
                return RoundGateStopVerdict.Block("5 完成被拒：本会话还没有通过的自验（3 + verdict=pass），先逐条对照原始需求自验。");

            if (StrictMode && !UserAcceptanceHandedOff)
                return RoundGateStopVerdict.Block("严格模式：完成前必须先选 4 用户验收，把 ○ 待用户验收清单移交用户。");
        }

        if (recorded == RoundGateChoice.Degrade && !UserAcceptanceHandedOff)
            return RoundGateStopVerdict.Block("9 降级交付被拒：先选 4 用户验收，把降级方案、取舍与影响移交用户确认。");

        return RoundGateStopVerdict.Allow;
    }

    private void ApplyTransition(RoundGateInvocation invocation) {
        var previous = LastChoice;
        switch (invocation.Choice) {
            case RoundGateChoice.Continue:
            case RoundGateChoice.NextTodo:
                SelfAcceptPassed = false;
                UserAcceptanceHandedOff = false;
                break;

            case RoundGateChoice.SelfAccept:
                SelfAcceptPassed = invocation.Verdict == RoundGateVerdict.Pass;
                SelfAcceptFailStreak = invocation.Verdict == RoundGateVerdict.Fail ? SelfAcceptFailStreak + 1 : 0;
                if (invocation.Verdict == RoundGateVerdict.Pass)
                    UserRejectStreak = 0;
                break;

            case RoundGateChoice.UserAccept:
                // 4 只是"把 ○ 清单移交用户"，不等于用户已通过；驳回计数留给 4 之后紧跟的 8
                UserAcceptanceHandedOff = true;
                break;

            case RoundGateChoice.Rework:
                SelfAcceptPassed = false;
                UserAcceptanceHandedOff = false;
                SelfAcceptFailStreak = 0;
                CountDefectRework(invocation.Subject);
                UserRejectStreak = previous == RoundGateChoice.UserAccept ? UserRejectStreak + 1 : 0;
                break;

            case RoundGateChoice.Replan:
                ReplanCount++;
                SelfAcceptPassed = false;
                SelfAcceptFailStreak = 0;
                UserAcceptanceHandedOff = false;
                break;

            default:
                // 5 完成 / 6 等候 / 9 降级 / 10 求助 / 0 阻塞上报：出口与挂起不改验证状态
                break;
        }
    }

    private void CountDefectRework(string? subject) {
        var key = NormalizeSubject(subject);
        _defectReworks[key] = DefectReworkCount(subject) + 1;
    }

    /// <summary>
    /// 熔断粘性解析 — 新熔断优先；旧熔断只有"已改选强制项"才解除，防止换个证据就绕过强制转出。
    /// </summary>
    private RoundGateFuse? ResolveFuse(RoundGateInvocation invocation) {
        if (EvaluateFuse(invocation.Subject) is { } computed)
            return computed;

        return Fuse is { } previous && invocation.Choice != previous.ForcedChoice ? previous : null;
    }

    private RoundGateFuse? EvaluateFuse(string? subject) {
        if (ReplanCount > MaxReplans)
            return FuseTo(RoundGateChoice.Escalate,
                $"重规划已 {ReplanCount} 次（上限 {MaxReplans}）— 方案层面需要外部输入");

        var defectCount = DefectReworkCount(subject);
        if (defectCount > MaxDefectReworks)
            return FuseTo(RoundGateChoice.Replan,
                $"缺陷 {NormalizeSubject(subject)} 已返工 {defectCount} 次（上限 {MaxDefectReworks}）— 怀疑方案本身有问题");

        if (SelfAcceptFailStreak >= MaxSelfAcceptFailStreak)
            return FuseTo(RoundGateChoice.Escalate,
                $"自验连续 {SelfAcceptFailStreak} 轮不通过 — 需要外部帮助");

        if (UserRejectStreak >= MaxUserRejectStreak)
            return FuseTo(RoundGateChoice.Replan,
                $"用户验收连续 {UserRejectStreak} 次被驳回 — 重新规划交付物");

        if (IdleStreak >= MaxIdleStreak)
            return FuseTo(RoundGateChoice.Replan,
                $"连续 {IdleStreak} 轮没有可验证证据（空转）— 重新规划");

        return null;
    }

    private static RoundGateFuse FuseTo(RoundGateChoice forced, string reason) => new() {
        ForcedChoice = forced,
        Reason = reason,
    };

    private static RoundGateRecommendation Recommend(
        RoundGateChoice choice, string reason, RoundGateChoice firstAlternative, RoundGateChoice secondAlternative) => new() {
        Choice = choice,
        Reason = reason,
        Alternatives = [firstAlternative, secondAlternative],
    };

    private static string NormalizeSubject(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? DefaultSubject : subject.Trim();
}

/// <summary>
/// 闸门推荐结果 — 一个默认 + 若干备选（不平铺全部选项）。
/// </summary>
public sealed record RoundGateRecommendation {
    /// <summary>推荐选项。</summary>
    public RoundGateChoice Choice { get; init; } = RoundGateChoice.Continue;

    /// <summary>推荐依据。</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>备选选项。</summary>
    public IReadOnlyList<RoundGateChoice> Alternatives { get; init; } = [];
}

/// <summary>
/// 熔断强制项 — 计数触顶后必须转出的选项。
/// </summary>
public sealed record RoundGateFuse {
    /// <summary>强制转入的选项。</summary>
    public RoundGateChoice ForcedChoice { get; init; } = RoundGateChoice.Replan;

    /// <summary>熔断原因。</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// 一次闸门记录后的状态快照。
/// </summary>
public sealed record RoundGateStep {
    /// <summary>本轮实选选项。</summary>
    public RoundGateChoice Choice { get; init; }

    /// <summary>闸门给出的推荐选项。</summary>
    public RoundGateRecommendation Recommendation { get; init; } = new();

    /// <summary>实选是否偏离推荐（申诉）。</summary>
    public bool IsAppeal { get; init; }

    /// <summary>申诉是否已超过上限（闸门开始锁定推荐项）。</summary>
    public bool AppealLocked { get; init; }

    /// <summary>当前生效的熔断强制项。</summary>
    public RoundGateFuse? Fuse { get; init; }
}

/// <summary>
/// 收尾裁决结果。
/// </summary>
public sealed record RoundGateStopVerdict {
    /// <summary>允许收尾。</summary>
    public static RoundGateStopVerdict Allow { get; } = new() { Allowed = true };

    /// <summary>是否允许本轮停止。</summary>
    public bool Allowed { get; init; }

    /// <summary>拦截原因（允许时为 null）。</summary>
    public string? Reason { get; init; }

    /// <summary>构造拦截结果。</summary>
    /// <param name="reason">拦截原因。</param>
    /// <returns>拦截裁决。</returns>
    public static RoundGateStopVerdict Block(string reason) => new() { Allowed = false, Reason = reason };
}
