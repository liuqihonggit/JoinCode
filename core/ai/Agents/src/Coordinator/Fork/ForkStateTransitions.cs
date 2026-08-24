namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 状态转换规则 — 集中定义所有合法转换的前置条件
/// <para>原 ForkSubAgentManager.ForkEntry 各方法内联直接赋值无校验，现统一提取为转换表</para>
/// <para>Running 可转 Completed/Failed/Cancelled，Completed 仅可转 Merged，其余为终态</para>
/// </summary>
public static class ForkStateTransitions
{
    private static readonly FrozenDictionary<ForkState, FrozenSet<ForkState>> Transitions =
        new Dictionary<ForkState, FrozenSet<ForkState>>
        {
            [ForkState.Running] = new HashSet<ForkState>
            {
                ForkState.Completed,
                ForkState.Failed,
                ForkState.Cancelled
            }.ToFrozenSet(),

            [ForkState.Completed] = new HashSet<ForkState>
            {
                ForkState.Merged
            }.ToFrozenSet(),

            [ForkState.Merged] = FrozenSet<ForkState>.Empty,
            [ForkState.Cancelled] = FrozenSet<ForkState>.Empty,
            [ForkState.Failed] = FrozenSet<ForkState>.Empty
        }.ToFrozenDictionary();

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法（相同状态不触发转换）
    /// </summary>
    /// <param name="current">当前状态</param>
    /// <param name="target">目标状态</param>
    /// <returns>合法返回 true，非法返回 false</returns>
    public static bool CanTransitionTo(ForkState current, ForkState target)
    {
        if (current == target)
        {
            return true;
        }

        return Transitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    /// <summary>
    /// 是否为终态 — Merged/Cancelled/Failed 为终态，不可再转换
    /// </summary>
    /// <param name="state">当前状态</param>
    /// <returns>终态返回 true，非终态返回 false</returns>
    public static bool IsTerminal(ForkState state) =>
        state is ForkState.Merged or ForkState.Cancelled or ForkState.Failed;
}
