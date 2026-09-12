namespace Core.Planning;

/// <summary>
/// 可持久化的 Plan 状态包装 — 用于跨进程（MCP 工具调用）共享活跃 plan 状态
/// 序列化到 ~/.jcc/plans/.active_plan_state.json
/// </summary>
public sealed class PersistablePlanState
{
    /// <summary>当前活跃计划ID</summary>
    public string? CurrentPlanId { get; set; }

    /// <summary>当前会话的 plan slug（用于 plan 文件路径复用）</summary>
    public string? CurrentSessionSlug { get; set; }

    /// <summary>活跃计划状态</summary>
    public PlanState? Plan { get; set; }
}
