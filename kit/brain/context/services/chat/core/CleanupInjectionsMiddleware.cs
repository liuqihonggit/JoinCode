namespace Core.Context;

/// <summary>
/// 清理注入中间件 — 清理预处理阶段注入的关键词和同义词
/// OnError=Continue：清理失败不影响管道继续执行
/// </summary>
[Register(typeof(IChatMiddleware), ServiceLifetime.Singleton)]
public sealed partial class CleanupInjectionsMiddleware : ServiceEntity, IChatMiddleware
{

    /// <summary>
    /// 初始化清理注入中间件
    /// </summary>
    /// <param name="preprocessor">聊天预处理器</param>
    /// <param name="logger">可选日志记录器</param>
    public CleanupInjectionsMiddleware(IChatPreprocessor preprocessor, ILogger<CleanupInjectionsMiddleware>? logger = null)
    {
        _preprocessor = preprocessor;
        _logger = logger;
    }
    private readonly IChatPreprocessor _preprocessor;
    private readonly ILogger<CleanupInjectionsMiddleware>? _logger;

    /// <summary>错误行为策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 透传下游事件 → 下游完成后清理注入
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

        if (context.PreprocessResult is not null)
        {
            await _preprocessor.CleanupInjectionsAsync(
                context.PreprocessResult.KeywordResult,
                context.PreprocessResult.SynonymInjectionIds, ct).ConfigureAwait(false);
        }
    }
}
