namespace Core.Context.Compact;

/// <summary>
/// 会话记忆压缩中间件 — 使用会话记忆进行压缩
/// </summary>
[Register(typeof(ICompactMiddleware), ServiceLifetime.Singleton)]
public sealed partial class SessionMemoryCompactMiddleware : ServiceEntity, ICompactMiddleware
{

    /// <summary>
    /// 初始化 <see cref="SessionMemoryCompactMiddleware"/> 实例
    /// </summary>
    /// <param name="sessionMemoryCompactService">会话记忆压缩服务</param>
    /// <param name="logger">可选日志记录器</param>
    public SessionMemoryCompactMiddleware(ISessionMemoryCompactService sessionMemoryCompactService, ILogger<SessionMemoryCompactMiddleware>? logger = null)
    {
        _sessionMemoryCompactService = sessionMemoryCompactService;
        _logger = logger;
    }
    private readonly ISessionMemoryCompactService _sessionMemoryCompactService;
    private readonly ILogger<SessionMemoryCompactMiddleware>? _logger;

    /// <summary>中间件异常时的行为：继续传递给下一个中间件</summary>
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
                    context.Request.Messages, context.PreCompactTokens, context.Request.TranscriptPath, ct).ConfigureAwait(false);

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
