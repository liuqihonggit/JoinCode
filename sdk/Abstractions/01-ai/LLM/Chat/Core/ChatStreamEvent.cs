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
    [EnumValue("complete")] Complete
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

    public T Match<T>(
        Func<string, T> onText,
        Func<string, T> onThinking,
        Func<string, string?, string?, T> onToolStart,
        Func<string, string?, string?, bool, StructuredPatchHunk[]?, T> onToolEnd,
        Func<string, string, string, T> onToolProgress,
        Func<int, int, string?, T> onLoopDetected,
        Func<string, T> onTimingSummary,
        Func<TokenUsage?, string?, T> onDone)
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
        Action<TokenUsage?, string?> onDone)
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
            default:
                throw new InvalidOperationException($"Unknown event type: {Type}");
        }
    }
}
