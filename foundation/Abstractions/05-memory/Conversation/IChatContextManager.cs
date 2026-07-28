
namespace JoinCode.Abstractions.Interfaces;

public interface IChatContextManager
{
    Task LoadContextAsync(CancellationToken cancellationToken = default);
    Task AddUserMessageAsync(string content, CancellationToken cancellationToken = default);
    Task AddCompactSummaryMessageAsync(string content, CancellationToken cancellationToken = default);
    Task AddAssistantMessageAsync(string content, CancellationToken cancellationToken = default);
    Task AddAssistantToolCallMessageAsync(string? content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken = default);
    Task AddToolResultMessageAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加包含多模态内容的工具结果消息 — 对齐 TS BashTool image output
    /// </summary>
    Task AddToolResultMessageAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, IReadOnlyList<ToolContent>? contentBlocks, CancellationToken cancellationToken = default);
    Task AddSystemMessageAsync(string content, CancellationToken cancellationToken = default);
    Task AddDynamicSystemMessageAsync(string content, CancellationToken cancellationToken = default);
    Task ClearDynamicSystemMessagesAsync(CancellationToken cancellationToken = default);
    Task ClearMessagesAsync(CancellationToken cancellationToken = default);
    Task UpdateSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default);
    Task<MessageList> GetMessageListAsync(CancellationToken cancellationToken = default);
    Task SaveContextAsync(CancellationToken cancellationToken = default);
    ContextFoldDecision DecideAfterUsage(TokenUsage usage, bool alreadyFoldedThisTurn = false);
    PreflightDecision DecidePreflight(IReadOnlyList<ToolSpec> toolSpecs);
    Task<ContextFoldResult> FoldIfNeededAsync(ContextFoldDecision decision, CancellationToken cancellationToken = default);
    int GetContextMaxTokens();

    /// <summary>
    /// 撤回最后一轮对话（SP-3 安全点）。尾部变更，前缀缓存仍命中。
    /// </summary>
    Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤回到指定消息索引（SP-5 安全点）。截断 [index, Count) 的消息。
    /// 缓存失效是预期行为，下一轮重新积累。
    /// </summary>
    Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空全部对话历史（SP-0 安全点）。前缀保留，历史清空。
    /// </summary>
    Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新当前工具规格列表（用于缓存失效检测）。MCP 工具同步时调用。
    /// </summary>
    Task UpdateToolSpecsAsync(IReadOnlyList<ToolSpec> toolSpecs, CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录当前前缀状态快照（LLM 请求前调用）。用于缓存失效两阶段检测的第一阶段。
    /// </summary>
    Task<PromptStateSnapshot> RecordPromptStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检测缓存失效（LLM 响应后调用）。两阶段检测的第二阶段，与请求前的快照对比。
    /// </summary>
    Task<CacheBreakResult> CheckCacheBreakAsync(PromptStateSnapshot snapshot, TokenUsage usage, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已发现的延迟工具集合。用于 Deferred Tools 请求构建。
    /// </summary>
    DiscoveredToolSet GetDiscoveredTools();

    /// <summary>
    /// 获取当前延迟工具列表。MCP 工具默认延迟。
    /// </summary>
    IReadOnlyList<DeferredToolInfo> GetDeferredTools();

    /// <summary>
    /// 从历史消息中提取已发现的工具名并更新 DiscoveredToolSet。
    /// </summary>
    Task SyncDiscoveredToolsFromHistoryAsync(CancellationToken cancellationToken = default);
}
