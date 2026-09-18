namespace Core.Context;

/// <summary>
/// 聊天上下文 Actor 命令类型 — 每个命令对应一个 ChatContextManager 操作，由 ChatContextActor Consumer 串行处理。
/// <para>TASK001: AsyncLock 迁移到 Actor 邮箱管道，消除 22 处显式锁。</para>
/// </summary>
public abstract record ChatContextCommand
{
    /// <summary>将异常设置到 Reply — AOT 友好（避免反射），由各子类重写</summary>
    public abstract void SetReplyException(Exception ex);
}

/// <summary>加载上下文 — 对应 LoadContextAsync</summary>
public sealed record LoadContextCmd(TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>添加用户消息 — 对应 AddUserMessageAsync</summary>
public sealed record AddUserMessageCmd(
    string Content,
    MessageOriginKind? OriginKind,
    TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>添加压缩摘要消息 — 对应 AddCompactSummaryMessageAsync</summary>
public sealed record AddCompactSummaryCmd(string Content, TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>添加助手消息 — 对应 AddAssistantMessageAsync</summary>
public sealed record AddAssistantMessageCmd(string Content, TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>添加助手工具调用消息 — 对应 AddAssistantToolCallMessageAsync</summary>
public sealed record AddAssistantToolCallCmd(
    string? Content,
    IReadOnlyDictionary<string, JsonElement> Metadata,
    TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>添加工具结果消息 — 对应 AddToolResultMessageAsync(重载1)</summary>
public sealed record AddToolResultCmd(
    string Content,
    IReadOnlyDictionary<string, JsonElement> Metadata,
    TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>添加含多模态内容的工具结果消息 — 对应 AddToolResultMessageAsync(重载2)</summary>
public sealed record AddToolResultWithBlocksCmd(
    string Content,
    IReadOnlyDictionary<string, JsonElement> Metadata,
    IReadOnlyList<ToolContent>? ContentBlocks,
    TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>添加系统消息 — 对应 AddSystemMessageAsync</summary>
public sealed record AddSystemMessageCmd(string Content, TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>添加动态系统消息 — 对应 AddDynamicSystemMessageAsync</summary>
public sealed record AddDynamicSystemMessageCmd(string Content, TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>清空动态系统消息 — 对应 ClearDynamicSystemMessagesAsync</summary>
public sealed record ClearDynamicSystemMessagesCmd(TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>清空对话消息 — 对应 ClearMessagesAsync</summary>
public sealed record ClearMessagesCmd(TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>更新静态系统提示词 — 对应 UpdateSystemPromptAsync</summary>
public sealed record UpdateSystemPromptCmd(string SystemPrompt, TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>获取组装后的消息列表 — 对应 GetMessageListAsync</summary>
public sealed record GetMessageListCmd(TaskCompletionSource<MessageList> Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>保存上下文 — 对应 SaveContextAsync</summary>
public sealed record SaveContextCmd(TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>折叠上下文 — 对应 FoldIfNeededAsync</summary>
public sealed record FoldIfNeededCmd(
    ContextFoldDecision Decision,
    string? AgentId,
    TaskCompletionSource<ContextFoldResult> Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>撤回最后一轮对话 — 对应 RewindLastTurnAsync</summary>
public sealed record RewindLastTurnCmd(TaskCompletionSource<RewindResult> Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>撤回到指定消息索引 — 对应 RewindToMessageIndexAsync</summary>
public sealed record RewindToMessageIndexCmd(int MessageIndex, TaskCompletionSource<RewindResult> Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>撤回到会话初始状态 — 对应 RewindToStartAsync</summary>
public sealed record RewindToStartCmd(TaskCompletionSource<RewindResult> Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>更新工具规格 — 对应 UpdateToolSpecsAsync</summary>
public sealed record UpdateToolSpecsCmd(
    IReadOnlyList<ToolSpec> ToolSpecs,
    TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>记录提示词前缀状态快照 — 对应 RecordPromptStateAsync</summary>
public sealed record RecordPromptStateCmd(
    string? AgentId,
    TaskCompletionSource<PromptStateSnapshot> Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>检测缓存失效 — 对应 CheckCacheBreakAsync</summary>
public sealed record CheckCacheBreakCmd(
    PromptStateSnapshot Snapshot,
    TokenUsage Usage,
    string? AgentId,
    TaskCompletionSource<CacheBreakResult> Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}

/// <summary>从历史同步已发现工具 — 对应 SyncDiscoveredToolsFromHistoryAsync</summary>
public sealed record SyncDiscoveredToolsFromHistoryCmd(TaskCompletionSource Reply) : ChatContextCommand
{
    /// <inheritdoc />
    public override void SetReplyException(Exception ex) => Reply.SetException(ex);
}
