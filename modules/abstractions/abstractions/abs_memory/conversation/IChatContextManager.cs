
namespace JoinCode.Abstractions.Interfaces;

public interface IChatContextManager {
    /// <summary>
    /// 当前会话标识 — 对齐 TS getSessionId()
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// 当前对话日志条目数 — 供 transcript 增量持久化取快照差量（进入轮次时记录，结束时取 [start..]）
    /// </summary>
    int CurrentMessageCount { get; }

    /// <summary>切换会话 — 按 sessionId 隔离对话历史，切回时自动恢复对应桶</summary>
    void SwitchSession(string sessionId);

    /// <summary>加载上下文。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task LoadContextAsync(CancellationToken cancellationToken = default);
    /// <summary>添加用户消息。</summary>
    /// <param name="content">消息内容。</param>
    /// <param name="originKind">消息来源类型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddUserMessageAsync(string content, MessageOriginKind? originKind = null, CancellationToken cancellationToken = default);
    /// <summary>添加压缩摘要消息。</summary>
    /// <param name="content">摘要内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddCompactSummaryMessageAsync(string content, CancellationToken cancellationToken = default);
    /// <summary>添加助手消息。</summary>
    /// <param name="content">消息内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddAssistantMessageAsync(string content, CancellationToken cancellationToken = default);
    /// <summary>添加助手工具调用消息。</summary>
    /// <param name="content">消息内容。</param>
    /// <param name="metadata">元数据字典。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddAssistantToolCallMessageAsync(string? content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken = default);
    /// <summary>添加工具结果消息。</summary>
    /// <param name="content">消息内容。</param>
    /// <param name="metadata">元数据字典。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddToolResultMessageAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加包含多模态内容的工具结果消息 — 对齐 TS BashTool image output
    /// </summary>
    Task AddToolResultMessageAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, IReadOnlyList<ToolContent>? contentBlocks, CancellationToken cancellationToken = default);
    /// <summary>添加系统消息。</summary>
    /// <param name="content">消息内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddSystemMessageAsync(string content, CancellationToken cancellationToken = default);
    /// <summary>添加动态系统消息。</summary>
    /// <param name="content">消息内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddDynamicSystemMessageAsync(string content, CancellationToken cancellationToken = default);
    /// <summary>清空动态系统消息。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ClearDynamicSystemMessagesAsync(CancellationToken cancellationToken = default);
    /// <summary>清空所有消息。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ClearMessagesAsync(CancellationToken cancellationToken = default);
    /// <summary>更新系统提示词。</summary>
    /// <param name="systemPrompt">系统提示词。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task UpdateSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default);
    /// <summary>获取消息列表。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<MessageList> GetMessageListAsync(CancellationToken cancellationToken = default);
    /// <summary>保存上下文。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveContextAsync(CancellationToken cancellationToken = default);
    /// <summary>根据用量决定折叠策略。</summary>
    /// <param name="usage">Token 用量。</param>
    /// <param name="alreadyFoldedThisTurn">本轮是否已折叠。</param>
    ContextFoldDecision DecideAfterUsage(TokenUsage usage, bool alreadyFoldedThisTurn = false);
    /// <summary>预检决策。</summary>
    /// <param name="toolSpecs">工具规格列表。</param>
    PreflightDecision DecidePreflight(IReadOnlyList<ToolSpec> toolSpecs);
    /// <param name="decision">折叠决策。</param>
    /// <param name="agentId">代理标识，null 表示主代理。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ContextFoldResult> FoldIfNeededAsync(ContextFoldDecision decision, string? agentId = null, CancellationToken cancellationToken = default);
    /// <summary>获取上下文最大 Token 数。</summary>
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
    /// <param name="agentId">代理标识，null 表示主代理。不同代理维护独立的缓存检测基线。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<PromptStateSnapshot> RecordPromptStateAsync(string? agentId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检测缓存失效（LLM 响应后调用）。两阶段检测的第二阶段，与请求前的快照对比。
    /// </summary>
    /// <param name="snapshot">请求前记录的前缀状态快照。</param>
    /// <param name="usage">LLM 响应的 token 用量。</param>
    /// <param name="agentId">代理标识，null 表示主代理。需与 RecordPromptStateAsync 使用相同的 agentId。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<CacheBreakResult> CheckCacheBreakAsync(PromptStateSnapshot snapshot, TokenUsage usage, string? agentId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已发现的延迟工具集合。用于 Deferred Tools 请求构建。
    /// </summary>
    DiscoveredToolSet GetDiscoveredTools();

    /// <summary>
    /// 获取当前延迟工具列表。MCP 工具默认延迟。
    /// </summary>
    IEnumerable<DeferredToolInfo> GetDeferredTools();

    /// <summary>
    /// 从历史消息中提取已发现的工具名并更新 DiscoveredToolSet。
    /// </summary>
    Task SyncDiscoveredToolsFromHistoryAsync(CancellationToken cancellationToken = default);
}
