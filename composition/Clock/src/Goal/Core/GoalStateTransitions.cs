namespace Core.Goal;

/// <summary>
/// 目标状态转换规则 — 集中定义 GoalStatus 所有合法转换
/// <para>原 GoalEngine+GoalStateTransitionMiddleware 分散赋值,现统一提取为转换表</para>
/// <para>Pursuing 可转 Paused/Achieved/Unmet/BudgetLimited,Paused 仅可转 Pursuing/Unmet</para>
/// <para>Achieved/Unmet/BudgetLimited 仅可转 Pursuing(Start重新开始)或 Unmet(Clear放弃)</para>
/// </summary>
public static class GoalStateTransitions
{
    /// <summary>
    /// 状态转换位掩码表 — 索引为 (int)GoalStatus，值为目标状态位掩码。
    /// 替代 FrozenDictionary&lt;GoalStatus, FrozenSet&lt;GoalStatus&gt;&gt;，O(1) 数组索引 + 位运算无哈希查找。
    /// </summary>
    private static readonly int[] Transitions =
    [
        /* Pursuing=0 */ BitMask.Of(GoalStatus.Paused, GoalStatus.Achieved, GoalStatus.Unmet, GoalStatus.BudgetLimited, GoalStatus.Pursuing),
        /* Paused=1 */ BitMask.Of(GoalStatus.Pursuing, GoalStatus.Unmet),
        /* Achieved=2 */ BitMask.Of(GoalStatus.Pursuing, GoalStatus.Unmet),
        /* Unmet=3 */ BitMask.Of(GoalStatus.Pursuing, GoalStatus.Unmet),
        /* BudgetLimited=4 */ BitMask.Of(GoalStatus.Pursuing, GoalStatus.Unmet)
    ];

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(GoalStatus current, GoalStatus target)
    {
        if (current == target)
        {
            return true;
        }

        return BitMask.Contains(Transitions[(int)current], target);
    }

    /// <summary>
    /// 是否为终态 — Achieved/Unmet/BudgetLimited 为终态（可重新 Start 但不可自动转出）
    /// </summary>
    public static bool IsTerminal(GoalStatus state) =>
        state is GoalStatus.Achieved or GoalStatus.Unmet or GoalStatus.BudgetLimited;
}
