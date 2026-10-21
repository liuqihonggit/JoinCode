namespace JoinCode.Gui.Hosting;

/// <summary>
/// 后台代理快照 — 引擎运行列表的会话门面映射产物（IJccChatSession.GetBackgroundAgentsAsync 契约 DTO）。
/// </summary>
public sealed record BackgroundAgentInfo(
    string AgentId,
    string Name,
    string Description,
    AgentStatus State,
    DateTime? StartedAt,
    int ToolUseCount,
    long TokenCount,
    IReadOnlyList<AgentActivityEntry> Activities,
    string? LastActivityText,
    string? FinalOutput,
    bool? IsSuccess,
    long? ExecutionTimeMs,
    string? Role);
