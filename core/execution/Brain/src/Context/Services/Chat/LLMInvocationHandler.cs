using JoinCode.Abstractions.Diagnostics;

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
    /// <param name="streamingToolExecution">是否启用流式工具执行模式</param>
    /// <param name="ct">取消令牌</param>
    IAsyncEnumerable<ChatStreamEvent> InvokeLLMAsync(
        MessageList historySnapshot,
        ChatOptions? executionSettings,
        ChatMiddlewareContext context,
        int iterationIndex,
        IterationState iterState,
        bool streamingToolExecution = false,
        CancellationToken ct = default);
}

/// <summary>
/// LLM 调用处理器 — 封装LLM流式调用、块处理、首token延迟追踪、对话转储
/// </summary>
[Register(typeof(ILLMInvocationHandler))]
public sealed partial class LLMInvocationHandler : ServiceEntity, ILLMInvocationHandler
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
        bool streamingToolExecution = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var callId = _contextManager is ChatContextManager cm ? cm.NextCallId() : $"?.{iterationIndex}";
        iterState.CallId = callId;
        CallTrace.SetId(callId);
        try
        {
            var chatCompletionService = _kernel.GetChatCompletionService();

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

                var result = _chunkProcessor.ProcessChunk(chunk, iterState, streamingToolExecution);

                foreach (var evt in result.Events)
                {
                    yield return evt;
                }

                if (result.Action == ChunkAction.Break) break;
                if (result.Action == ChunkAction.Continue) continue;
            }

            context.Timing.StopLlmCall();
            context.Timing.LlmCallCount++;

            var textPreview = iterState.FullResponse.Length > 0
                ? $" | 预览={iterState.FullResponse.ToString(0, Math.Min(iterState.FullResponse.Length, 100))}"
                : "";
            Diag.WriteLine($"[LLM {callId}] #{iterationIndex} → {(iterState.ToolCallName is not null ? $"tool_call={iterState.ToolCallName}" : "纯文本")}, 文本={iterState.FullResponse.Length}字符{textPreview}, 模型={iterState.StreamModelId ?? "?"}, tokens={iterState.StreamUsage?.TotalTokens}");
        }
        finally
        {
            CallTrace.Clear();
        }
    }
}
