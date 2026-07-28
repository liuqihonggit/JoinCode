using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 清理注入中间件 — 清理预处理阶段注入的关键词和同义词
/// OnError=Continue：清理失败不影响管道继续执行
/// </summary>
[Register]
public sealed partial class CleanupInjectionsMiddleware : IChatMiddleware
{
    [Inject] private readonly IChatPreprocessor _preprocessor;
    [Inject] private readonly ILogger<CleanupInjectionsMiddleware>? _logger;

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
