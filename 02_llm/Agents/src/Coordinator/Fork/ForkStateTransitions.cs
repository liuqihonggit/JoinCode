namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 状态转换规则 — 集中定义所有合法转换的前置条件
/// <para>原 ForkSubAgentManager.ForkEntry 各方法内联直接赋值无校验，现统一提取为转换表</para>
/// <para>Running 可转 Completed/Failed/Cancelled，Completed 仅可转 Merged，其余为终态</para>
/// </summary>
public static class ForkStateTransitions
{
    /// <summary>
    /// 状态转换位掩码表 — 索引为 (int)ForkState，值为目标状态位掩码。
    /// 替代 FrozenDictionary&lt;ForkState, FrozenSet&lt;ForkState&gt;&gt;，O(1) 数组索引 + 位运算无哈希查找。
    /// </summary>
    private static readonly int[] Transitions =
    [
        /* Running=0 */ BitMask.Of(ForkState.Completed, ForkState.Failed, ForkState.Cancelled),
        /* Completed=1 */ BitMask.Of(ForkState.Merged),
        /* Merged=2 */ 0,
        /* Cancelled=3 */ 0,
        /* Failed=4 */ 0
    ];

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

        return BitMask.Contains(Transitions[(int)current], target);
    }

    /// <summary>
    /// 是否为终态 — Merged/Cancelled/Failed 为终态，不可再转换
    /// </summary>
    /// <param name="state">当前状态</param>
    /// <returns>终态返回 true，非终态返回 false</returns>
    public static bool IsTerminal(ForkState state) =>
        state is ForkState.Merged or ForkState.Cancelled or ForkState.Failed;
}
