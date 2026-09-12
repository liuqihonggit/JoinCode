namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 图节点类型
/// </summary>
public enum GoalNodeKind
{
    /// <summary>AI Agent 节点 — 调用 LLM</summary>
    [EnumValue("agent")] Agent,
    /// <summary>函数节点 — 调用代码逻辑</summary>
    [EnumValue("function")] Function,
    /// <summary>汇聚节点 — 等待所有上游完成后合并输出</summary>
    [EnumValue("join")] Join
}

/// <summary>
/// 图节点执行状态
/// </summary>
public enum GoalNodeStatus
{
    /// <summary>待执行</summary>
    [EnumValue("pending")] Pending,
    /// <summary>执行中</summary>
    [EnumValue("running")] Running,
    /// <summary>已完成</summary>
    [EnumValue("completed")] Completed,
    /// <summary>失败</summary>
    [EnumValue("failed")] Failed,
    /// <summary>跳过（条件路由未命中）</summary>
    [EnumValue("skipped")] Skipped
}

/// <summary>
/// 条件路由匹配模式
/// </summary>
public enum RouteMatchMode
{
    /// <summary>只匹配非空 Label ∈ Routes，空 Label 不匹配（默认）</summary>
    [EnumValue("conditional_only")] ConditionalOnly,
    /// <summary>只走空 Label 边（默认分支/fallback）</summary>
    [EnumValue("unconditional_only")] UnconditionalOnly,
    /// <summary>空 Label + 匹配的非空 Label 都走（fan-out 场景）</summary>
    [EnumValue("all")] All
}
