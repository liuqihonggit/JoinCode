namespace JoinCode.Abstractions.Interfaces;

public interface ITeammateReconnectService {
    /// <summary>从持久化存储恢复团队上下文。</summary>
    /// <param name="teamName">团队名称。</param>
    /// <param name="agentName">指定恢复的 Agent 名称（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TeamContext?> RestoreTeamContextAsync(string teamName, string? agentName = null, CancellationToken cancellationToken = default);

    /// <summary>从会话转录恢复团队上下文。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TeamContext?> RestoreFromTranscriptAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>重连指定队友。</summary>
    /// <param name="teamId">团队标识。</param>
    /// <param name="agentId">Agent 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ReconnectResult> ReconnectTeammateAsync(string teamId, string agentId, CancellationToken cancellationToken = default);

    /// <summary>重连团队中所有已断开的队友。</summary>
    /// <param name="teamId">团队标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ReconnectResult> ReconnectAllDisconnectedAsync(string teamId, CancellationToken cancellationToken = default);
}

public sealed class TeamContext {
    /// <summary>获取团队名称。</summary>
    public required string TeamName { get; init; }
    /// <summary>获取团队标识。</summary>
    public required string TeamId { get; init; }
    /// <summary>获取主 Agent 标识。</summary>
    public string? LeadAgentId { get; init; }
    /// <summary>获取自身 Agent 标识。</summary>
    public string? SelfAgentId { get; init; }
    /// <summary>获取自身 Agent 名称。</summary>
    public string? SelfAgentName { get; init; }
    /// <summary>获取是否为主 Agent。</summary>
    public bool IsLeader { get; init; }
    /// <summary>获取队友字典（AgentId → 队友条目）。</summary>
    public Dictionary<string, ReconnectTeammateEntry> Teammates { get; init; } = new(StringComparer.Ordinal);
}

public sealed class ReconnectTeammateEntry {
    /// <summary>获取 Agent 标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取队友名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取颜色标识。</summary>
    public string? Color { get; init; }
    /// <summary>获取是否活跃。</summary>
    public bool IsActive { get; init; } = true;
    /// <summary>获取工作模式。</summary>
    public string? Mode { get; init; }
    /// <summary>获取会话标识。</summary>
    public string? SessionId { get; init; }
    /// <summary>获取工作树路径。</summary>
    public string? WorktreePath { get; init; }
}

public sealed class ReconnectResult {
    /// <summary>获取 Agent 标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取重连状态。</summary>
    public required ReconnectStatus Status { get; init; }
    /// <summary>获取尝试次数。</summary>
    public int AttemptCount { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? ErrorMessage { get; init; }
}

public enum ReconnectStatus {
    [EnumValue("success")] Success,
    [EnumValue("failed")] Failed,
    [EnumValue("max_retries_exceeded")] MaxRetriesExceeded,
    [EnumValue("cancelled")] Cancelled
}