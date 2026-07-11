using JoinCode.Abstractions.Attributes;

namespace Core.Context.Compact;

/// <summary>
/// 会话记忆压缩中间件 — 使用会话记忆进行压缩
/// </summary>
[Register(typeof(ICompactMiddleware))]
public sealed partial class SessionMemoryCompactMiddleware : ICompactMiddleware
{
    [Inject] private readonly ISessionMemoryCompactService _sessionMemoryCompactService;
    [Inject] private readonly ILogger<SessionMemoryCompactMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(CompactContext context, MiddlewareDelegate<CompactContext> next, CancellationToken ct)
    {
        // 仅在 Auto 触发模式下尝试会话记忆压缩
        if (context.Request.Trigger == CompactTrigger.Auto)
        {
            try
            {
                var result = await _sessionMemoryCompactService.TrySessionMemoryCompactAsync(
                    context.Request.Messages, context.PreCompactTokens, ct).ConfigureAwait(false);

                if (result is not null)
                {
                    context.Result = result;
                    context.ConsecutiveFailures = 0;
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[SessionMemoryCompact] 会话记忆压缩失败，继续下一个中间件");
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
