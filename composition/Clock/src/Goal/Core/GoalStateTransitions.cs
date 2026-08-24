namespace Core.Goal;

/// <summary>
/// 目标状态转换规则 — 集中定义 GoalStatus 所有合法转换
/// <para>原 GoalEngine+GoalStateTransitionMiddleware 分散赋值,现统一提取为转换表</para>
/// <para>Pursuing 可转 Paused/Achieved/Unmet/BudgetLimited,Paused 仅可转 Pursuing/Unmet</para>
/// <para>Achieved/Unmet/BudgetLimited 仅可转 Pursuing(Start重新开始)或 Unmet(Clear放弃)</para>
/// </summary>
public static class GoalStateTransitions
{
    private static readonly FrozenDictionary<GoalStatus, FrozenSet<GoalStatus>> Transitions =
        new Dictionary<GoalStatus, FrozenSet<GoalStatus>>
        {
            [GoalStatus.Pursuing] = new HashSet<GoalStatus>
            {
                GoalStatus.Paused,
                GoalStatus.Achieved,
                GoalStatus.Unmet,
                GoalStatus.BudgetLimited,
                GoalStatus.Pursuing
            }.ToFrozenSet(),

            [GoalStatus.Paused] = new HashSet<GoalStatus>
            {
                GoalStatus.Pursuing,
                GoalStatus.Unmet
            }.ToFrozenSet(),

            [GoalStatus.Achieved] = new HashSet<GoalStatus>
            {
                GoalStatus.Pursuing,
                GoalStatus.Unmet
            }.ToFrozenSet(),

            [GoalStatus.Unmet] = new HashSet<GoalStatus>
            {
                GoalStatus.Pursuing,
                GoalStatus.Unmet
            }.ToFrozenSet(),

            [GoalStatus.BudgetLimited] = new HashSet<GoalStatus>
            {
                GoalStatus.Pursuing,
                GoalStatus.Unmet
            }.ToFrozenSet()
        }.ToFrozenDictionary();

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(GoalStatus current, GoalStatus target)
    {
        if (current == target)
        {
            return true;
        }

        return Transitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    /// <summary>
    /// 是否为终态 — Achieved/Unmet/BudgetLimited 为终态（可重新 Start 但不可自动转出）
    /// </summary>
    public static bool IsTerminal(GoalStatus state) =>
        state is GoalStatus.Achieved or GoalStatus.Unmet or GoalStatus.BudgetLimited;
}
