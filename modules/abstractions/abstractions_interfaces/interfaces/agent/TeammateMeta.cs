namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Teammate 元信息 — 挂到 SubAgentContext 作为可选子对象,替代独立的 TeammateContext AsyncLocal
/// </summary>
public sealed record TeammateMeta {
    /// <summary>Teammate 显示名称</summary>
    public required string AgentName { get; init; }
    /// <summary>团队名称</summary>
    public required string TeamName { get; init; }
    /// <summary>颜色标识</summary>
    public string? Color { get; init; }
    /// <summary>是否需要 PlanMode 审批</summary>
    public bool PlanModeRequired { get; init; }
    /// <summary>父会话 ID</summary>
    public required string ParentSessionId { get; init; }
    /// <summary>是否进程内执行</summary>
    public bool IsInProcess { get; init; } = true;
}