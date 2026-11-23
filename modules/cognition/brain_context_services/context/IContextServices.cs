namespace Core.Context;

/// <summary>
/// 上下文折叠服务 — 单一数据源: 折叠状态 (_deferredFoldCount, _consecutiveNoProgressFolds)
/// <para>依赖: ISessionStore, IToolSpecStore, ICacheBreakStore, IContextWindowResolver, ContextFoldExecutor?</para>
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// </summary>
internal interface IContextFoldService {
    /// <summary>根据 token 用量决定是否折叠</summary>
    /// <param name="usage">Token 用量</param>
    /// <param name="alreadyFoldedThisTurn">本轮是否已折叠</param>
    /// <returns>折叠决策</returns>
    ContextFoldDecision DecideAfterUsage(TokenUsage usage, bool alreadyFoldedThisTurn);

    /// <summary>预判是否需要折叠，基于当前消息和工具规格估算 token 占用</summary>
    /// <param name="toolSpecs">工具规格列表</param>
    /// <param name="messages">当前消息列表</param>
    /// <returns>预检决策</returns>
    PreflightDecision DecidePreflight(IReadOnlyList<ToolSpec> toolSpecs, IReadOnlyList<ApiMessage> messages);

    /// <summary>根据折叠决策执行上下文折叠操作</summary>
    /// <param name="decision">折叠决策</param>
    /// <param name="agentId">代理标识，null 表示主代理</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>折叠结果</returns>
    Task<ContextFoldResult> FoldIfNeededAsync(
        ContextFoldDecision decision,
        string? agentId,
        CancellationToken cancellationToken);
}

/// <summary>
/// 撤回服务 — 无独立数据源，操作 ISessionStore + IPromptStore
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// </summary>
internal interface IRewindService {
    /// <summary>撤回最后一轮对话（SP-3 安全点），移除最近的用户-助手消息对</summary>
    /// <returns>撤回结果</returns>
    RewindResult RewindLastTurn();

    /// <summary>撤回到指定消息索引（SP-5 安全点），移除该索引之后的所有消息</summary>
    /// <param name="messageIndex">保留的消息数量</param>
    /// <returns>撤回结果</returns>
    RewindResult RewindToMessageIndex(int messageIndex);

    /// <summary>撤回到会话初始状态（SP-0 安全点），清空所有对话消息</summary>
    /// <returns>撤回结果</returns>
    RewindResult RewindToStart();
}

/// <summary>
/// 上下文持久化服务 — 单一数据源: IStateService + ISessionMetaStore
/// <para>依赖: ISessionStore, IPromptStore, SessionStats?</para>
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// </summary>
internal interface IContextPersistenceService {
    /// <summary>从持久化存储加载聊天上下文，恢复系统提示词和对话历史</summary>
    /// <param name="sessionId">当前会话标识，用于加载会话元数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task LoadAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>将当前聊天上下文持久化保存，包括系统提示词、对话历史和会话统计</summary>
    /// <param name="sessionId">当前会话标识，用于保存会话元数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SaveAsync(string sessionId, CancellationToken cancellationToken);
}
