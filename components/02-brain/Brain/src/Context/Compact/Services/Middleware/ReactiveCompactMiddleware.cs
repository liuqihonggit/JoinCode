using JoinCode.Abstractions.Attributes;

namespace Core.Context.Compact;

/// <summary>
/// 响应式压缩中间件 — 处理 prompt-too-long 等错误触发的压缩
/// </summary>
[Register(typeof(ICompactMiddleware))]
public sealed partial class ReactiveCompactMiddleware : ICompactMiddleware
{
    [Inject] private readonly IReactiveCompactService _reactiveCompactService;
    [Inject] private readonly ILogger<ReactiveCompactMiddleware>? _logger;

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
