using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 保存上下文中间件 — 持久化聊天上下文到存储
/// OnError=Continue：保存失败不影响管道继续执行
/// </summary>
[Register]
public sealed partial class SaveContextMiddleware : IChatMiddleware
{
    [Inject] private readonly IChatContextManager _contextManager;
    [Inject] private readonly ILogger<SaveContextMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 透传下游事件 → 下游完成后保存上下文
    /// 不缓冲事件流，保证流式响应的实时性
    /// 计时摘要由 ChatTimingMiddleware 统一输出（受 JCC_VERBOSE 控制）
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
            await _contextManager.SaveContextAsync(ct).ConfigureAwait(false);

        context.Timing.StopPostProcess();
        context.Timing.StopTotal();
    }
}
