namespace Core.Context;

/// <summary>
/// 对话历史存储 — 单一数据源: AppendOnlyLog (按SessionId分桶)
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// <para>日后可将 ImmutableHamT 分桶结构替换为 HAMT 无锁并发结构，接口不变。</para>
/// </summary>
internal interface ISessionStore {
    /// <summary>当前会话的消息条数</summary>
    int Count { get; }

    /// <summary>当前会话的对话日志 — 供 FoldExecutor/SnipStaleToolResults 等直接操作</summary>
    AppendOnlyLog Log { get; }

    /// <summary>导出当前会话的消息快照 — 零分配，返回内部 List 引用</summary>
    IReadOnlyList<ApiMessage> ToMessages();

    /// <summary>追加消息到当前会话</summary>
    void Append(ApiMessage message);

    /// <summary>原地压缩，替换全部消息（折叠/压缩后重写日志）</summary>
    void CompactInPlace(IReadOnlyList<ApiMessage> messages);

    /// <summary>裁剪最后一轮对话，返回移除条数</summary>
    int TrimLastTurn();

    /// <summary>截断到指定索引，返回移除条数</summary>
    int TruncateTo(int index);

    /// <summary>切换会话 — 按 sessionId 隔离对话历史</summary>
    void SwitchSession(string sessionId);
}

/// <summary>
/// 系统提示词存储 — 单一数据源: SystemPromptStore
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// </summary>
internal interface IPromptStore {
    /// <summary>静态系统提示词</summary>
    string StaticPrompt { get; }

    /// <summary>更新静态提示词，清空缓存</summary>
    void Update(string systemPrompt);

    /// <summary>添加动态系统消息，清空缓存</summary>
    void AddDynamic(string content);

    /// <summary>清空所有动态系统消息</summary>
    void ClearDynamic();

    /// <summary>重置缓存（回退到初始状态时调用）</summary>
    void ResetCache();

    /// <summary>获取动态消息拼接内容（用于缓存破坏检测）</summary>
    string GetDynamicContent();

    /// <summary>获取或创建缓存的系统消息列表 — 动态消息未变时复用缓存</summary>
    IReadOnlyList<ApiMessage> GetOrCreateCachedSystemMessages();
}

/// <summary>
/// 工具规格存储 — 单一数据源: List&lt;ToolSpec&gt; + List&lt;DeferredToolInfo&gt; + DiscoveredToolSet
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// </summary>
internal interface IToolSpecStore {
    /// <summary>当前工具规格列表</summary>
    IReadOnlyList<ToolSpec> CurrentSpecs { get; }

    /// <summary>延迟加载的工具列表（主要是 MCP 工具）</summary>
    IReadOnlyList<DeferredToolInfo> DeferredTools { get; }

    /// <summary>已发现工具集合</summary>
    DiscoveredToolSet DiscoveredTools { get; }

    /// <summary>更新工具规格，同步识别 MCP 延迟工具</summary>
    void UpdateSpecs(IReadOnlyList<ToolSpec> toolSpecs);

    /// <summary>从历史消息中提取已发现的工具名并同步到已发现工具集合</summary>
    Task SyncDiscoveredToolsFromHistoryAsync(IReadOnlyList<ApiMessage> history);
}

/// <summary>
/// 缓存破坏检测存储 — 单一数据源: ImmutableHamT&lt;string, CacheBreakDetector&gt;
/// <para>按 agentId 隔离，主代理(null)和子代理互不干扰基线。</para>
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// </summary>
internal interface ICacheBreakStore {
    /// <summary>记录提示词前缀状态快照</summary>
    /// <param name="agentId">代理标识，null 表示主代理</param>
    /// <param name="prefix">不变前缀（系统提示与工具规格）</param>
    /// <param name="dynamicContent">动态内容</param>
    /// <param name="messages">对话消息序列</param>
    /// <returns>当前请求的状态快照</returns>
    PromptStateSnapshot RecordPromptState(
        string? agentId,
        ImmutablePrefix prefix,
        string dynamicContent,
        IReadOnlyList<ApiMessage> messages);

    /// <summary>检测缓存失效</summary>
    /// <param name="agentId">代理标识，null 表示主代理</param>
    /// <param name="snapshot">上次记录的状态快照</param>
    /// <param name="currentPrefix">当前不变前缀</param>
    /// <param name="currentDynamicContent">当前动态内容</param>
    /// <param name="usage">本次请求的 token 使用情况</param>
    /// <param name="messages">当前对话消息序列</param>
    /// <returns>缓存破坏检测结果</returns>
    CacheBreakResult CheckCacheBreak(
        string? agentId,
        PromptStateSnapshot snapshot,
        ImmutablePrefix currentPrefix,
        string currentDynamicContent,
        TokenUsage usage,
        IReadOnlyList<ApiMessage> messages);

    /// <summary>通知压缩发生，重置缓存基线</summary>
    void NotifyCompaction(string? agentId);
}
