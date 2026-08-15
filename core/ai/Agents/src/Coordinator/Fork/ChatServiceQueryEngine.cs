namespace Core.Agents.Coordinator;

/// <summary>
/// ChatService → IQueryEngine 适配器 — 主代理用 ChatService 13中间件管道，子代理用普通 QueryEngine
/// AgentBase.ExecuteStreamAsync 统一调用 _queryEngine.QueryAsync，通过注入不同实例区分管道
/// chatHistory 参数被忽略 — ChatService 内部通过 IChatContextManager 管理对话历史
/// </summary>
public sealed class ChatServiceQueryEngine : IQueryEngine
{
    private readonly IChatService _chatService;

    public ChatServiceQueryEngine(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// 同步查询 — 委托到 ChatService.SendMessageAsync
    /// </summary>
    public async Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        return await _chatService.SendMessageAsync(query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 流式查询 — 委托到 ChatService.StreamWithEventsAsync，转换 ChatStreamEvent → QueryStreamChunk
    /// </summary>
    public async IAsyncEnumerable<QueryStreamChunk> QueryAsync(
        string userInput,
        MessageList chatHistory,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in _chatService.StreamWithEventsAsync(userInput, cancellationToken).ConfigureAwait(false))
        {
            var chunk = FromChatStreamEvent(evt);
            if (chunk is not null) yield return chunk;
        }
    }

    /// <summary>
    /// 流式查询（带选项）— 忽略 options，ChatService 内部管理选项
    /// </summary>
    public async IAsyncEnumerable<QueryStreamChunk> QueryAsync(
        string userInput,
        MessageList chatHistory,
        QueryOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in _chatService.StreamWithEventsAsync(userInput, cancellationToken).ConfigureAwait(false))
        {
            var chunk = FromChatStreamEvent(evt);
            if (chunk is not null) yield return chunk;
        }
    }

    /// <summary>
    /// 主代理不使用 IQueryService — 抛出异常
    /// </summary>
    public JoinCode.Abstractions.LLM.IQueryService GetChatCompletionService()
        => throw new NotSupportedException("MainAgent 不使用 IQueryService");

    /// <summary>
    /// 主代理不使用 IChatClient — 抛出异常
    /// </summary>
    public JoinCode.Abstractions.LLM.IChatClient GetKernel()
        => throw new NotSupportedException("MainAgent 不使用 IChatClient");

    /// <summary>
    /// ChatStreamEvent → QueryStreamChunk 转换
    /// </summary>
    private static QueryStreamChunk? FromChatStreamEvent(ChatStreamEvent evt)
    {
        return evt.Type switch
        {
            ChatStreamEventType.Content => new QueryStreamChunk
            {
                Type = AgentStreamChunkType.Content,
                Content = evt.Content
            },
            ChatStreamEventType.Thinking => new QueryStreamChunk
            {
                Type = AgentStreamChunkType.Thinking,
                ThinkingContent = evt.ThinkingContent
            },
            ChatStreamEventType.ToolCallStart => new QueryStreamChunk
            {
                Type = AgentStreamChunkType.ToolCallStart,
                ToolName = evt.ToolName,
                ToolCallId = evt.ToolCallId,
                ToolArguments = evt.ToolArguments
            },
            ChatStreamEventType.ToolCallEnd => new QueryStreamChunk
            {
                Type = AgentStreamChunkType.ToolCallEnd,
                ToolName = evt.ToolName,
                ToolCallId = evt.ToolCallId,
                ToolResultText = evt.ToolResultText,
                IsToolError = evt.IsToolError,
                StructuredPatch = evt.StructuredPatch
            },
            ChatStreamEventType.ToolProgress => new QueryStreamChunk
            {
                Type = AgentStreamChunkType.ToolProgress,
                ToolName = evt.ToolName,
                ToolCallId = evt.ToolCallId,
                ProgressMessage = evt.ProgressMessage,
                ProgressType = evt.ProgressType
            },
            ChatStreamEventType.LoopDetected => new QueryStreamChunk
            {
                Type = AgentStreamChunkType.LoopDetected,
                LoopTriggerCount = evt.LoopTriggerCount,
                LoopStartIndex = evt.LoopStartIndex,
                Content = evt.Content
            },
            ChatStreamEventType.TimingSummary => new QueryStreamChunk
            {
                Type = AgentStreamChunkType.TimingSummary,
                Content = evt.Content
            },
            ChatStreamEventType.Complete => new QueryStreamChunk
            {
                Type = AgentStreamChunkType.Complete,
                Usage = evt.Usage,
                ModelId = evt.ModelId
            },
            _ => null
        };
    }
}
