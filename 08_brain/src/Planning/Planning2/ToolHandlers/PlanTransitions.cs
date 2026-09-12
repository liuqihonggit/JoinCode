namespace Core.Planning;

/// <summary>
/// 计划状态转换规则 — 集中定义 PlanStatus 所有合法转换
/// <para>原 PlanModeManager 各方法内联直接赋值，现统一提取为转换表</para>
/// <para>Draft 可转 AwaitingApproval/Executing/Cancelled，Executing 可转 Completed/Failed/Cancelled</para>
/// </summary>
public static class PlanStateTransitions
{
    /// <summary>
    /// 状态转换位掩码表 — 索引为 (int)PlanStatus，值为目标状态位掩码。
    /// 替代 FrozenDictionary&lt;PlanStatus, FrozenSet&lt;PlanStatus&gt;&gt;，O(1) 数组索引 + 位运算无哈希查找。
    /// </summary>
    private static readonly int[] Transitions =
    [
        /* Draft=0 */ BitMask.Of(PlanStatus.AwaitingApproval, PlanStatus.Executing, PlanStatus.Cancelled),
        /* AwaitingApproval=1 */ BitMask.Of(PlanStatus.Executing, PlanStatus.Cancelled),
        /* Executing=2 */ BitMask.Of(PlanStatus.Completed, PlanStatus.Failed, PlanStatus.Cancelled),
        /* Completed=3 */ 0,
        /* Cancelled=4 */ 0,
        /* Failed=5 */ 0
    ];

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(PlanStatus current, PlanStatus target)
    {
        if (current == target)
        {
            return true;
        }

        return BitMask.Contains(Transitions[(int)current], target);
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
    /// <summary>
    /// 状态转换位掩码表 — 索引为 (int)PlanStepStatus，值为目标状态位掩码。
    /// 替代 FrozenDictionary&lt;PlanStepStatus, FrozenSet&lt;PlanStepStatus&gt;&gt;，O(1) 数组索引 + 位运算无哈希查找。
    /// </summary>
    private static readonly int[] Transitions =
    [
        /* Pending=0 */ BitMask.Of(PlanStepStatus.Approved, PlanStepStatus.Rejected, PlanStepStatus.Skipped),
        /* Approved=1 */ BitMask.Of(PlanStepStatus.Executing),
        /* Rejected=2 */ BitMask.Of(PlanStepStatus.Pending, PlanStepStatus.Approved),
        /* Executing=3 */ BitMask.Of(PlanStepStatus.Completed, PlanStepStatus.Failed),
        /* Completed=4 */ 0,
        /* Failed=5 */ 0,
        /* Skipped=6 */ 0
    ];

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(PlanStepStatus current, PlanStepStatus target)
    {
        if (current == target)
        {
            return true;
        }

        return BitMask.Contains(Transitions[(int)current], target);
    }

    /// <summary>
    /// 是否为终态 — Completed/Failed/Skipped 为终态
    /// </summary>
    public static bool IsTerminal(PlanStepStatus state) =>
        state is PlanStepStatus.Completed or PlanStepStatus.Failed or PlanStepStatus.Skipped;
}
