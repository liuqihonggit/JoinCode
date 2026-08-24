namespace Core.Planning;

/// <summary>
/// 计划状态转换规则 — 集中定义 PlanStatus 所有合法转换
/// <para>原 PlanModeManager 各方法内联直接赋值，现统一提取为转换表</para>
/// <para>Draft 可转 AwaitingApproval/Executing/Cancelled，Executing 可转 Completed/Failed/Cancelled</para>
/// </summary>
public static class PlanStateTransitions
{
    private static readonly FrozenDictionary<PlanStatus, FrozenSet<PlanStatus>> Transitions =
        new Dictionary<PlanStatus, FrozenSet<PlanStatus>>
        {
            [PlanStatus.Draft] = new HashSet<PlanStatus>
            {
                PlanStatus.AwaitingApproval,
                PlanStatus.Executing,
                PlanStatus.Cancelled
            }.ToFrozenSet(),

            [PlanStatus.AwaitingApproval] = new HashSet<PlanStatus>
            {
                PlanStatus.Executing,
                PlanStatus.Cancelled
            }.ToFrozenSet(),

            [PlanStatus.Executing] = new HashSet<PlanStatus>
            {
                PlanStatus.Completed,
                PlanStatus.Failed,
                PlanStatus.Cancelled
            }.ToFrozenSet(),

            [PlanStatus.Completed] = FrozenSet<PlanStatus>.Empty,
            [PlanStatus.Cancelled] = FrozenSet<PlanStatus>.Empty,
            [PlanStatus.Failed] = FrozenSet<PlanStatus>.Empty
        }.ToFrozenDictionary();

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(PlanStatus current, PlanStatus target)
    {
        if (current == target)
        {
            return true;
        }

        return Transitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    /// <summary>
    /// 是否为终态 — Completed/Cancelled/Failed 为终态
    /// </summary>
    public static bool IsTerminal(PlanStatus state) =>
        state is PlanStatus.Completed or PlanStatus.Cancelled or PlanStatus.Failed;
}

/// <summary>
/// 计划步骤状态转换规则 — 集中定义 PlanStepStatus 所有合法转换
/// <para>Pending 可转 Approved/Rejected/Skipped，Approved 可转 Executing，Executing 可转 Completed/Failed</para>
/// <para>Rejected 可转 Pending(修改后重置)或 Approved(重新批准)</para>
/// </summary>
public static class PlanStepTransitions
{
    private static readonly FrozenDictionary<PlanStepStatus, FrozenSet<PlanStepStatus>> Transitions =
        new Dictionary<PlanStepStatus, FrozenSet<PlanStepStatus>>
        {
            [PlanStepStatus.Pending] = new HashSet<PlanStepStatus>
            {
                PlanStepStatus.Approved,
                PlanStepStatus.Rejected,
                PlanStepStatus.Skipped
            }.ToFrozenSet(),

            [PlanStepStatus.Approved] = new HashSet<PlanStepStatus>
            {
                PlanStepStatus.Executing
            }.ToFrozenSet(),

            [PlanStepStatus.Rejected] = new HashSet<PlanStepStatus>
            {
                PlanStepStatus.Pending,
                PlanStepStatus.Approved
            }.ToFrozenSet(),

            [PlanStepStatus.Executing] = new HashSet<PlanStepStatus>
            {
                PlanStepStatus.Completed,
                PlanStepStatus.Failed
            }.ToFrozenSet(),

            [PlanStepStatus.Completed] = FrozenSet<PlanStepStatus>.Empty,
            [PlanStepStatus.Failed] = FrozenSet<PlanStepStatus>.Empty,
            [PlanStepStatus.Skipped] = FrozenSet<PlanStepStatus>.Empty
        }.ToFrozenDictionary();

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(PlanStepStatus current, PlanStepStatus target)
    {
        if (current == target)
        {
            return true;
        }

        return Transitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    /// <summary>
    /// 是否为终态 — Completed/Failed/Skipped 为终态
    /// </summary>
    public static bool IsTerminal(PlanStepStatus state) =>
        state is PlanStepStatus.Completed or PlanStepStatus.Failed or PlanStepStatus.Skipped;
}
