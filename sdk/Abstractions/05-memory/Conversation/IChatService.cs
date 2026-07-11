namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 聊天消息记录
/// </summary>
public sealed record ApiMessageRecord
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public interface IChatService : IAsyncDisposable
{
    Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> SendMessageStreamAsync(string message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatStreamEvent> StreamWithEventsAsync(string message, CancellationToken cancellationToken = default);
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiMessageRecord>> GetMessageListAsync(CancellationToken cancellationToken = default);
    Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤回最后一轮对话（SP-3 安全点）
    /// </summary>
    Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤回到指定消息索引（SP-5 安全点）
    /// </summary>
    Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空全部对话历史（SP-0 安全点）
    /// </summary>
    Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载历史消息到当前会话（用于 /resume 恢复会话）
    /// </summary>
    Task LoadSessionMessagesAsync(IReadOnlyList<ApiMessageRecord> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// 压缩对话历史（用于 /compact 上下文压缩）
    /// 清空现有消息，将摘要作为系统消息注入
    /// </summary>
    Task CompactHistoryAsync(string summary, CancellationToken cancellationToken = default);
}
