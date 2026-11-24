namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 聊天消息记录
/// </summary>
public sealed record ApiMessageRecord {
    /// <summary>获取角色。</summary>
    public required string Role { get; init; }
    /// <summary>获取内容。</summary>
    public required string Content { get; init; }
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public interface IChatService : IAsyncDisposable {
    /// <summary>异步发送消息并返回完整响应。</summary>
    Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default);
    /// <summary>异步发送消息并返回流式响应。</summary>
    IAsyncEnumerable<string> SendMessageStreamAsync(string message, CancellationToken cancellationToken = default);
    /// <summary>异步发送消息并以事件流形式返回响应。</summary>
    IAsyncEnumerable<ChatStreamEvent> StreamWithEventsAsync(string message, CancellationToken cancellationToken = default, IEventConsumer? eventConsumer = null);
    /// <summary>异步清空对话历史。</summary>
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 开始新会话（会话翻页，ADR 0167 / TASK135 B1）— 生成新 sessionId，旧代消息与 transcript
    /// 原样留在磁盘，当前桶切到新代。与 <c>/clear</c> 同语义。
    /// **工具路径必须走这里**：只调 <see cref="ClearHistoryAsync"/> 会「清内存不清磁盘」，
    /// 模型对用户说已清空、下次 resume 全部回来。
    /// </summary>
    /// <param name="projectPath">新会话所属项目路径（落 meta.json）；null 用当前目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新会话标识。</returns>
    Task<string> StartNewConversationAsync(string? projectPath = null, CancellationToken cancellationToken = default);
    /// <summary>异步获取消息列表。</summary>
    Task<IReadOnlyList<ApiMessageRecord>> GetMessageListAsync(CancellationToken cancellationToken = default);
    /// <summary>异步设置系统提示词。</summary>
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