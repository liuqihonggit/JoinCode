namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 代理元数据 - 保存到 .meta.json sidecar 文件
/// </summary>
public sealed class AgentMetadata
{
    public required string AgentId { get; init; }
    public string? AgentType { get; init; }
    public string? Description { get; init; }
    public string? WorktreePath { get; init; }
    public string? ModelName { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; init; }
    public string? Status { get; init; }

    /// <summary>
    /// 总 Token 使用量
    /// </summary>
    public int? TokenUsage { get; init; }

    /// <summary>
    /// 工具调用次数
    /// </summary>
    public int? ToolCallCount { get; init; }

    /// <summary>
    /// 错误信息（失败时）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 执行时长（毫秒）
    /// </summary>
    public long? DurationMs { get; init; }
}

/// <summary>
/// 代理 Transcript 服务 - 管理 SubAgent 的对话记录持久化
/// </summary>
public interface IAgentTranscriptService
{
    /// <summary>
    /// 追加代理对话条目
    /// </summary>
    Task AppendEntryAsync(string sessionId, string agentId, JoinCode.Abstractions.LLM.Chat.TranscriptEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量追加代理对话条目
    /// </summary>
    Task AppendEntriesAsync(string sessionId, string agentId, IReadOnlyList<JoinCode.Abstractions.LLM.Chat.TranscriptEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载代理对话记录
    /// </summary>
    Task<IReadOnlyList<JoinCode.Abstractions.LLM.Chat.TranscriptEntry>> LoadTranscriptAsync(string sessionId, string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存代理元数据
    /// </summary>
    Task SaveMetadataAsync(string sessionId, AgentMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载代理元数据
    /// </summary>
    Task<AgentMetadata?> LoadMetadataAsync(string sessionId, string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出会话下所有代理的元数据
    /// </summary>
    Task<IReadOnlyList<AgentMetadata>> ListMetadataAsync(string sessionId, CancellationToken cancellationToken = default);
}
