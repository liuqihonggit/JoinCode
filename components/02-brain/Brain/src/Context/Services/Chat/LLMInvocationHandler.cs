namespace Core.Context;

/// <summary>
/// LLM 调用处理器接口 — 负责LLM流式调用和事件生成
/// </summary>
public interface ILLMInvocationHandler
{
    /// <summary>
    /// 调用LLM并返回流式事件，同时填充迭代状态
    /// </summary>
    /// <param name="historySnapshot">当前对话历史快照</param>
    /// <param name="executionSettings">LLM 调用选项</param>
    /// <param name="context">聊天中间件上下文</param>
    /// <param name="iterationIndex">当前迭代索引</param>
    /// <param name="iterState">由调用方创建的迭代状态对象，本方法在流式处理过程中填充</param>
    /// <param name="ct">取消令牌</param>
    IAsyncEnumerable<ChatStreamEvent> InvokeLLMAsync(
        MessageList historySnapshot,
        ChatOptions? executionSettings,
        ChatMiddlewareContext context,
        int iterationIndex,
        IterationState iterState,
        CancellationToken ct);
}

/// <summary>
/// LLM 调用处理器 — 封装LLM流式调用、块处理、首token延迟追踪、对话转储
/// </summary>
[Register(typeof(ILLMInvocationHandler))]
public sealed class LLMInvocationHandler : ILLMInvocationHandler
{
    private readonly IChatClient _kernel;
    private readonly IChatStreamChunkProcessor _chunkProcessor;
    private readonly IChatContextManager _contextManager;
    private readonly QueryLoopServices? _services;
    [Inject] private readonly ILogger<LLMInvocationHandler>? _logger;

    public LLMInvocationHandler(
        IChatClient kernel,
        IChatStreamChunkProcessor chunkProcessor,
        IChatContextManager contextManager,
        QueryLoopServices? services = null,
        ILogger<LLMInvocationHandler>? logger = null)
    {
        _kernel = kernel;
        _chunkProcessor = chunkProcessor;
        _contextManager = contextManager;
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeLLMAsync(
        MessageList historySnapshot,
        ChatOptions? executionSettings,
        ChatMiddlewareContext context,
        int iterationIndex,
        IterationState iterState,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var chatCompletionService = _kernel.GetChatCompletionService();

        // 对话消息列表转储
        var dumpSessionId = (_contextManager is ChatContextManager c) ? c.SessionId : "default";
        _services?.FileContextService?.DumpMessageList(historySnapshot, dumpSessionId, context.ConversationTurn, iterationIndex);

        context.Timing.StartLlmCall();
        var isFirstChunk = true;

        await foreach (var chunk in chatCompletionService.GetStreamEventContentsAsync(
            historySnapshot, executionSettings, _kernel, ct).ConfigureAwait(false))
        {
            if (isFirstChunk)
            {
                isFirstChunk = false;
                context.Timing.FirstTokenLatencyMs = context.Timing.LlmTotalMs;
            }

            var result = _chunkProcessor.ProcessChunk(chunk, iterState);

            foreach (var evt in result.Events)
            {
                yield return evt;
            }

            if (result.Action == ChunkAction.Break) break;
            if (result.Action == ChunkAction.Continue) continue;
        }

        context.Timing.StopLlmCall();
        context.Timing.LlmCallCount++;
    }
}
