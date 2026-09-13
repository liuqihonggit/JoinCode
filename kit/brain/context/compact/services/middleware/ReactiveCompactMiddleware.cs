namespace Core.Context.Compact;

/// <summary>
/// 响应式压缩中间件 — 处理 prompt-too-long 等错误触发的压缩
/// </summary>
[Register(typeof(ICompactMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ReactiveCompactMiddleware : ServiceEntity, ICompactMiddleware
{

    /// <summary>
    /// 初始化 <see cref="ReactiveCompactMiddleware"/> 实例
    /// </summary>
    /// <param name="reactiveCompactService">响应式压缩服务</param>
    /// <param name="logger">可选日志记录器</param>
    public ReactiveCompactMiddleware(IReactiveCompactService reactiveCompactService, ILogger<ReactiveCompactMiddleware>? logger = null)
    {
        _reactiveCompactService = reactiveCompactService;
        _logger = logger;
    }
    private readonly IReactiveCompactService _reactiveCompactService;
    private readonly ILogger<ReactiveCompactMiddleware>? _logger;

    /// <summary>中间件异常时的行为：继续传递给下一个中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(CompactContext context, MiddlewareDelegate<CompactContext> next, CancellationToken ct)
    {
        // 响应式压缩仅在 Reactive 触发模式下执行
        if (context.Request.Trigger == CompactTrigger.Reactive)
        {
            try
            {
                var result = await _reactiveCompactService.RunReactiveCompactAsync(
                    context.Request.Messages,
                    context.Request.CustomInstructions ?? "",
                    ct).ConfigureAwait(false);

                if (result.Compacted)
                {
                    context.Result = result;
                    context.ConsecutiveFailures = 0;
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[ReactiveCompact] 响应式压缩失败，继续下一个中间件");
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
