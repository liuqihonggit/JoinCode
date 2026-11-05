namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 代理元数据 - 保存到 .meta.json sidecar 文件
/// </summary>
public sealed class AgentMetadata {
    /// <summary>获取代理标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取代理角色。</summary>
    public AgentRole Role { get; init; }
    /// <summary>获取执行器变体。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>获取代理类型。</summary>
    public string? AgentType { get; init; }
    /// <summary>获取代理描述。</summary>
    public string? Description { get; init; }
    /// <summary>获取工作树路径。</summary>
    public string? WorktreePath { get; init; }
    /// <summary>获取模型名称。</summary>
    public string? ModelName { get; init; }
    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>获取完成时间。</summary>
    public DateTime? CompletedAt { get; init; }
    /// <summary>获取状态。</summary>
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
public interface IAgentTranscriptService {
    /// <summary>
    /// 追加代理对话条目
    /// </summary>
    Task AppendEntryAsync(string sessionId, string agentId, JoinCode.Abstractions.LLM.Chat.TranscriptEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量追加代理对话条目
    /// </summary>
    Task AppendEntriesAsync(string sessionId, string agentId, IEnumerable<JoinCode.Abstractions.LLM.Chat.TranscriptEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载代理对话记录
    /// </summary>
    Task<IEnumerable<JoinCode.Abstractions.LLM.Chat.TranscriptEntry>> LoadTranscriptAsync(string sessionId, string agentId, CancellationToken cancellationToken = default);

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
    Task<IEnumerable<AgentMetadata>> ListMetadataAsync(string sessionId, CancellationToken cancellationToken = default);
}