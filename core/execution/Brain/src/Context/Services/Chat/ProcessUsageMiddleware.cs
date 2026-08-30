namespace Core.Context;

/// <summary>
/// 用量处理中间件 — 处理 Token 用量统计
/// OnError=Continue：用量处理失败不影响管道继续执行
/// </summary>
[Register(typeof(IChatMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ProcessUsageMiddleware : ServiceEntity, IChatMiddleware
{

    public ProcessUsageMiddleware(IChatUsageProcessor usageProcessor, ILogger<ProcessUsageMiddleware>? logger = null)
    {
        _usageProcessor = usageProcessor;
        _logger = logger;
    }
    private readonly IChatUsageProcessor _usageProcessor;
    private readonly ILogger<ProcessUsageMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 透传下游事件 → 下游完成后处理用量
    /// 不缓冲事件流，保证流式响应的实时性
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }

        if (context.FinalUsage is not null && context.PromptSnapshot is not null)
        {
            await _usageProcessor.ProcessUsageAsync(
                context.FinalUsage, context.FinalModelId, context.PromptSnapshot, ct).ConfigureAwait(false);
        }
    }
}
