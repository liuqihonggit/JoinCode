namespace Core.Context;

/// <summary>
/// LLM 调用处理器接口 — 负责LLM流式调用和事件生成
/// </summary>
public interface ILLMInvocationHandler {
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
[Register(typeof(ILLMInvocationHandler), ServiceLifetime.Singleton)]
public sealed partial class LLMInvocationHandler : ServiceEntity, ILLMInvocationHandler {
    private readonly IChatClient _kernel;
    private readonly IChatStreamChunkProcessor _chunkProcessor;
    private readonly IChatContextManager _contextManager;
    private readonly QueryLoopServices? _services;
    private readonly IChatFileContextService? _fileContextService;
    private readonly ILogger<LLMInvocationHandler>? _logger;

    /// <summary>
    /// 初始化 LLM 调用处理器
    /// </summary>
    /// <param name="kernel">聊天客户端</param>
    /// <param name="chunkProcessor">流式块处理器</param>
    /// <param name="contextManager">聊天上下文管理器</param>
    /// <param name="services">查询循环可选服务聚合</param>
    /// <param name="fileContextService">消息转储服务 — 直接注入而不依赖 QueryLoopServices 聚合；该聚合在未装配可选服务时整个为 null，实测导致 JCC_DUMP_MESSAGES 从不落盘</param>
    /// <param name="logger">可选日志记录器</param>
    public LLMInvocationHandler(
        IChatClient kernel,
        IChatStreamChunkProcessor chunkProcessor,
        IChatContextManager contextManager,
        QueryLoopServices? services = null,
        IChatFileContextService? fileContextService = null,
        ILogger<LLMInvocationHandler>? logger = null) {
        _kernel = kernel;
        _chunkProcessor = chunkProcessor;
        _contextManager = contextManager;
        _services = services;
        _fileContextService = fileContextService;
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
        [EnumeratorCancellation] CancellationToken ct = default) {
        var callId = _contextManager is ChatContextManager cm ? cm.NextCallId() : $"?.{iterationIndex}";
        iterState.CallId = callId;
        using var scope = CallTrace.EnterScope(callId);

        var chatCompletionService = _kernel.GetChatCompletionService();

        var dumpSessionId = (_contextManager is ChatContextManager c) ? c.SessionId : global::Core.Utils.SessionIdFactory.DefaultSessionId;
        // 必须 await：`_ = ...` 在一次性 -p 会话里会被进程退出打断，实测整轮跑完一个 dump 文件都没有。
        // 优先用直接注入的服务；QueryLoopServices 聚合在轻量宿主里可能为 null 或没装配 FileContextService。
        // 方法内部有 JCC_DUMP_MESSAGES 开关，关闭时只是一次 if 判断。
        var dumper = _fileContextService ?? _services?.FileContextService;
        if (dumper is not null)
            await dumper.DumpMessageListAsync(historySnapshot, dumpSessionId, context.ConversationTurn, iterationIndex).ConfigureAwait(false);

        context.Timing.StartLlmCall();
        var isFirstChunk = true;

        await foreach (var chunk in chatCompletionService.GetStreamEventContentsAsync(
            historySnapshot, executionSettings, _kernel, ct).ConfigureAwait(false)) {
            if (isFirstChunk) {
                isFirstChunk = false;
                context.Timing.FirstTokenLatencyMs = context.Timing.LlmTotalMs;
            }

            if (chunk.RawEvent is not null && context.EventConsumer is not null) {
                try {
                    using var doc = System.Text.Json.JsonDocument.Parse(chunk.RawEvent);
                    context.EventConsumer.OnStreamEvent(doc.RootElement.Clone());
                } catch (System.Text.Json.JsonException ex) {
                    _logger?.LogWarning(ex, "[LLM {CallId}] stream_event RawEvent JSON 解析失败", callId);
                }
            }

            var result = _chunkProcessor.ProcessChunk(chunk, iterState, streamingToolExecution);

            foreach (var evt in result.Events) {
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
}