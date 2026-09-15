namespace Core.Goal;

/// <summary>
/// 节点完成后的整体目标状态决策。
/// <para>从 GoalGraphEngine 嵌套枚举提取为命名空间级别，供 RetryHandler 等独立类引用。</para>
/// </summary>
internal enum NodeCompletionOutcome
{
    /// <summary>继续执行后续节点</summary>
    Continue,

    /// <summary>目标已达成，终止执行</summary>
    GoalAchieved,

    /// <summary>目标未达成，终止执行</summary>
    GoalUnmet,
}
