using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 保存上下文中间件 — 持久化聊天上下文到存储
/// OnError=Continue：保存失败不影响管道继续执行
/// </summary>
[Register]
public sealed partial class SaveContextMiddleware : ServiceEntity, IChatMiddleware
{

    public SaveContextMiddleware(IChatContextManager contextManager, ILogger<SaveContextMiddleware>? logger = null)
    {
        _contextManager = contextManager;
        _logger = logger;
    }
    private readonly IChatContextManager _contextManager;
    private readonly ILogger<SaveContextMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 透传下游事件 → 下游完成后保存上下文
    /// 不缓冲事件流，保证流式响应的实时性
    /// 计时摘要由 ChatTimingMiddleware 统一输出（受 JCC_DEBUGLOG 控制）
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

        context.Timing.StartPostProcess();

        if (!context.IsDryRun)
            await SaveContextWithRetryAsync(context, ct).ConfigureAwait(false);

        context.Timing.StopPostProcess();
        context.Timing.StopTotal();
    }

    /// <summary>
    /// 保存上下文 — 失败重试一次并记录显式错误，避免持久化失败静默丢失会话
    /// 管道 OnError=Continue 仅记录，此处主动重试 + 显式日志作为纵深防御
    /// </summary>
    private async Task SaveContextWithRetryAsync(ChatMiddlewareContext context, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await _contextManager.SaveContextAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt == 1)
            {
                _logger?.LogError(ex, "[SaveContext] 上下文保存失败（尝试 {Attempt}/2），即将重试", attempt);
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[SaveContext] 上下文保存失败（尝试 {Attempt}/2），会话可能丢失", attempt);
            }
        }
    }
}
