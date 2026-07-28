using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 会话保存中间件 — 管理操作完成后统一保存上下文
/// 无业务服务依赖，仅使用 context.ContextManager
/// </summary>
[Register(typeof(IChatAdminMiddleware))]
public sealed partial class SessionSaveMiddleware : IChatAdminMiddleware
{
    [Inject] private readonly ILogger<SessionSaveMiddleware>? _logger;


    /// <summary>
    /// 先执行下游中间件，再统一保存上下文
    /// </summary>
    public async Task InvokeAsync(ChatAdminContext context, MiddlewareDelegate<ChatAdminContext> next, CancellationToken ct)
    {
        // 先执行下游（终端处理器或其他中间件）
        await next(context, ct).ConfigureAwait(false);

        // 仅在无错误时保存上下文
        if (context.Error is null)
        {
            try
            {
                await context.ContextManager.SaveContextAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[SessionSave] 保存上下文失败");
            }
        }
    }
}
