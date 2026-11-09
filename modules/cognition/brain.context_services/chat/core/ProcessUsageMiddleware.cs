namespace Core.Context;

/// <summary>
/// 用量处理中间件 — 处理 Token 用量统计
/// OnError=Continue：用量处理失败不影响管道继续执行
/// </summary>
[Register(typeof(IChatMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ProcessUsageMiddleware : ServiceEntity, IChatMiddleware {

    /// <summary>
    /// 初始化用量处理中间件
    /// </summary>
    /// <param name="usageProcessor">聊天用量处理器</param>
    /// <param name="logger">可选日志记录器</param>
    public ProcessUsageMiddleware(IChatUsageProcessor usageProcessor, ILogger<ProcessUsageMiddleware>? logger = null) {
        _usageProcessor = usageProcessor;
        _logger = logger;
    }
    private readonly IChatUsageProcessor _usageProcessor;
    private readonly ILogger<ProcessUsageMiddleware>? _logger;

    /// <summary>错误行为策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 透传下游事件 → 下游完成后处理用量
    /// 不缓冲事件流，保证流式响应的实时性
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct) {
        await foreach (var evt in next(context, ct).ConfigureAwait(false)) {
            yield return evt;
        }

        if (context.FinalUsage is not null && context.PromptSnapshot is not null) {
            await _usageProcessor.ProcessUsageAsync(
                context.FinalUsage, context.FinalModelId, context.PromptSnapshot, context.AgentId, ct).ConfigureAwait(false);
        }
    }
}