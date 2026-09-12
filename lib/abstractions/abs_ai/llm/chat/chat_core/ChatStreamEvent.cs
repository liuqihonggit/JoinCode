namespace JoinCode.Abstractions.LLM.Chat;

public enum ChatStreamEventType
{
    [EnumValue("content")] Content,
    [EnumValue("thinking")] Thinking,
    [EnumValue("toolCallStart")] ToolCallStart,
    [EnumValue("toolCallEnd")] ToolCallEnd,
    [EnumValue("toolProgress")] ToolProgress,
    [EnumValue("loopDetected")] LoopDetected,
    [EnumValue("timingSummary")] TimingSummary,
    [EnumValue("complete")] Complete,
    [EnumValue("tombstone")] Tombstone,
    [EnumValue("agentStarted")] AgentStarted,
    [EnumValue("agentFinished")] AgentFinished
}

public sealed class ChatStreamEvent
{
    public ChatStreamEventType Type { get; init; }
    public string? Content { get; init; }
    public string? ThinkingContent { get; init; }
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolArguments { get; init; }
    public string? ToolResultText { get; init; }
    public bool IsToolError { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? ModelId { get; init; }

    /// <summary>
    /// 结构化 Patch — 对齐 TS FileEditOutput.structuredPatch
    /// 仅 ToolCallEnd 事件携带，传递给 UI 渲染
    /// </summary>
    public StructuredPatchHunk[]? StructuredPatch { get; init; }

    /// <summary>
    /// 工具进度消息 — 对齐 TS WebSearchTool onProgress
    /// 仅 ToolProgress 事件携带，传递搜索进度给 TUI 层
    /// </summary>
    public string? ProgressMessage { get; init; }

    /// <summary>
    /// 进度类型标识 — 对齐 TS WebSearchProgress.type
    /// "query_update" 或 "search_results_received"
    /// </summary>
    public string? ProgressType { get; init; }

    /// <summary>
    /// 循环检测触发次数 — 仅 LoopDetected 事件携带
    /// </summary>
    public int LoopTriggerCount { get; init; }

    /// <summary>
    /// 循环起点索引 — 仅 LoopDetected 事件携带，用于截断
    /// </summary>
    public int LoopStartIndex { get; init; }

    /// <summary>
    /// 子代理 ID — 非 null 表示该事件是子代理内部活动（GUI 按此路由到 SubAgentRunTracker），
    /// 对齐 TS onProgress 附着 toolUseID 的模式；主对话事件此字段为 null
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>子代理显示名（如 "explore"）— 仅 AgentStarted 事件携带</summary>
    public string? AgentName { get; init; }

    /// <summary>子代理任务描述 — 仅 AgentStarted 事件携带</summary>
    public string? AgentDescription { get; init; }

    /// <summary>子代理角色（researcher/coder/...）— 仅 AgentStarted 事件携带</summary>
    public string? AgentRole { get; init; }

    /// <summary>子代理是否成功 — 仅 AgentFinished 事件携带</summary>
    public bool? AgentSuccess { get; init; }

    /// <summary>子代理执行时长（毫秒）— 仅 AgentFinished 事件携带</summary>
    public long? AgentExecutionTimeMs { get; init; }

    /// <summary>是否为子代理活动事件（AgentId 非 null 即是，含 Started/Finished 与中间活动）</summary>
    public bool IsSubAgentActivity => AgentId is not null;

    public static ChatStreamEvent Text(string content) => new()
    {
        Type = ChatStreamEventType.Content,
        Content = content
    };

    public static ChatStreamEvent Thinking(string thinkingContent) => new()
    {
        Type = ChatStreamEventType.Thinking,
        ThinkingContent = thinkingContent
    };

    public static ChatStreamEvent ToolStart(string toolName, string? toolCallId = null, string? arguments = null) => new()
    {
        Type = ChatStreamEventType.ToolCallStart,
        ToolName = toolName,
        ToolCallId = toolCallId,
        ToolArguments = arguments
    };

    public static ChatStreamEvent ToolEnd(string toolName, string? resultText = null, string? toolCallId = null, bool isError = false, StructuredPatchHunk[]? structuredPatch = null) => new()
    {
        Type = ChatStreamEventType.ToolCallEnd,
        ToolName = toolName,
        ToolCallId = toolCallId,
        ToolResultText = resultText,
        IsToolError = isError,
        StructuredPatch = structuredPatch
    };

    /// <summary>
    /// 工具进度事件 — 对齐 TS WebSearchTool onProgress
    /// 传递搜索进度（query_update/search_results_received）给 TUI 层
    /// </summary>
    public static ChatStreamEvent ToolProgress(string toolName, string progressType, string progressMessage, string? toolCallId = null) => new()
    {
        Type = ChatStreamEventType.ToolProgress,
        ToolName = toolName,
        ToolCallId = toolCallId,
        ProgressType = progressType,
        ProgressMessage = progressMessage
    };

    public static ChatStreamEvent LoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern = null) => new()
    {
        Type = ChatStreamEventType.LoopDetected,
        LoopTriggerCount = triggerCount,
        LoopStartIndex = loopStartIndex,
        Content = repeatedPattern
    };

    public static ChatStreamEvent TimingSummary(string summary) => new()
    {
        Type = ChatStreamEventType.TimingSummary,
        Content = summary
    };

    public static ChatStreamEvent Done(TokenUsage? usage = null, string? modelId = null) => new()
    {
        Type = ChatStreamEventType.Complete,
        Usage = usage,
        ModelId = modelId
    };

    /// <summary>
    /// 子代理启动事件 — 携带身份元数据（名称/描述/角色），GUI 据此创建运行卡片
    /// </summary>
    public static ChatStreamEvent AgentStarted(string agentId, string? name = null, string? description = null, string? role = null) => new()
    {
        Type = ChatStreamEventType.AgentStarted,
        AgentId = agentId,
        AgentName = name,
        AgentDescription = description,
        AgentRole = role
    };

    /// <summary>
    /// 子代理结束事件 — 携带成功标记/执行时长/token 用量/最终输出，GUI 定格统计卡片
    /// </summary>
    public static ChatStreamEvent AgentFinished(
        string agentId, bool success, long? executionTimeMs = null, TokenUsage? usage = null, string? finalOutput = null) => new()
    {
        Type = ChatStreamEventType.AgentFinished,
        AgentId = agentId,
        AgentSuccess = success,
        AgentExecutionTimeMs = executionTimeMs,
        Usage = usage,
        Content = finalOutput
    };

    public T Match<T>(
        Func<string, T> onText,
        Func<string, T> onThinking,
        Func<string, string?, string?, T> onToolStart,
        Func<string, string?, string?, bool, StructuredPatchHunk[]?, T> onToolEnd,
        Func<string, string, string, T> onToolProgress,
        Func<int, int, string?, T> onLoopDetected,
        Func<string, T> onTimingSummary,
        Func<TokenUsage?, string?, T> onDone,
        Func<string, T>? onAgentStarted = null,
        Func<string, bool?, T>? onAgentFinished = null)
    {
        return Type switch
        {
            ChatStreamEventType.Content => onText(Content ?? string.Empty),
            ChatStreamEventType.Thinking => onThinking(ThinkingContent ?? string.Empty),
            ChatStreamEventType.ToolCallStart => onToolStart(ToolName ?? string.Empty, ToolCallId, ToolArguments),
            ChatStreamEventType.ToolCallEnd => onToolEnd(ToolName ?? string.Empty, ToolResultText, ToolCallId, IsToolError, StructuredPatch),
            ChatStreamEventType.ToolProgress => onToolProgress(ToolName ?? string.Empty, ProgressType ?? "", ProgressMessage ?? ""),
            ChatStreamEventType.LoopDetected => onLoopDetected(LoopTriggerCount, LoopStartIndex, Content),
            ChatStreamEventType.TimingSummary => onTimingSummary(Content ?? ""),
            ChatStreamEventType.Complete => onDone(Usage, ModelId),
            ChatStreamEventType.AgentStarted => onAgentStarted is not null
                ? onAgentStarted(AgentId ?? string.Empty)
                : throw new InvalidOperationException($"AgentStarted 事件需要 {nameof(onAgentStarted)} 回调"),
            ChatStreamEventType.AgentFinished => onAgentFinished is not null
                ? onAgentFinished(AgentId ?? string.Empty, AgentSuccess)
                : throw new InvalidOperationException($"AgentFinished 事件需要 {nameof(onAgentFinished)} 回调"),
            _ => throw new InvalidOperationException($"Unknown event type: {Type}")
        };
    }

    public void Switch(
        Action<string> onText,
        Action<string> onThinking,
        Action<string, string?, string?> onToolStart,
        Action<string, string?, string?, bool, StructuredPatchHunk[]?> onToolEnd,
        Action<string, string, string> onToolProgress,
        Action<int, int, string?> onLoopDetected,
        Action<string> onTimingSummary,
        Action<TokenUsage?, string?> onDone,
        Action<string>? onAgentStarted = null,
        Action<string, bool?>? onAgentFinished = null)
    {
        switch (Type)
        {
            case ChatStreamEventType.Content:
                onText(Content ?? string.Empty);
                break;
            case ChatStreamEventType.Thinking:
                onThinking(ThinkingContent ?? string.Empty);
                break;
            case ChatStreamEventType.ToolCallStart:
                onToolStart(ToolName ?? string.Empty, ToolCallId, ToolArguments);
                break;
            case ChatStreamEventType.ToolCallEnd:
                onToolEnd(ToolName ?? string.Empty, ToolResultText, ToolCallId, IsToolError, StructuredPatch);
                break;
            case ChatStreamEventType.ToolProgress:
                onToolProgress(ToolName ?? string.Empty, ProgressType ?? "", ProgressMessage ?? "");
                break;
            case ChatStreamEventType.LoopDetected:
                onLoopDetected(LoopTriggerCount, LoopStartIndex, Content);
                break;
            case ChatStreamEventType.TimingSummary:
                onTimingSummary(Content ?? "");
                break;
            case ChatStreamEventType.Complete:
                onDone(Usage, ModelId);
                break;
            // Agent 事件在未提供回调时静默忽略 — 保证既有消费方（AskClarifyCommand/SessionController）零改动兼容
            case ChatStreamEventType.AgentStarted:
                onAgentStarted?.Invoke(AgentId ?? string.Empty);
                break;
            case ChatStreamEventType.AgentFinished:
                onAgentFinished?.Invoke(AgentId ?? string.Empty, AgentSuccess);
                break;
            default:
                throw new InvalidOperationException($"Unknown event type: {Type}");
        }
    }
}
