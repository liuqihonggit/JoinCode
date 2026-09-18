namespace Core.Bridge;


/// <summary>
/// 工作健康检查中间件 — 拦截 healthcheck 工作类型并短路返回，同时更新已存在会话的访问令牌
/// </summary>
[Register(typeof(IHandleWorkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorkHealthcheckMiddleware : ServiceEntity, IHandleWorkMiddleware
{

    /// <summary>
    /// 构造工作健康检查中间件
    /// </summary>
    /// <param name="apiClient">桥接 API 客户端</param>
    /// <param name="logger">日志器 — null 表示不记录日志</param>
    public WorkHealthcheckMiddleware(BridgeApiClient apiClient, ILogger<WorkHealthcheckMiddleware>? logger = null)
    {
        _apiClient = apiClient;
        _logger = logger;
    }
    private readonly ILogger<WorkHealthcheckMiddleware>? _logger;
    private readonly BridgeApiClient _apiClient;


    /// <summary>
    /// 处理工作项 — healthcheck 类型短路确认，已存在会话更新令牌后短路，否则传递给下游中间件
    /// </summary>
    /// <param name="ctx">工作处理上下文</param>
    /// <param name="next">下游中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct)
    {
        if (string.Equals(ctx.Work.WorkType, "healthcheck", StringComparison.OrdinalIgnoreCase))
        {
            if (ctx.SessionIngressToken is not null)
            {
                await _apiClient.AcknowledgeWorkAsync(
                    ctx.EnvironmentId ?? "", ctx.Work.WorkId, ctx.SessionIngressToken, ct).ConfigureAwait(false);
            }
            _logger?.LogDebug("BridgeMain: healthcheck received");
            ctx.ShortCircuited = true;
            return;
        }

        var existingHandle = ctx.Tracker.Sessions.GetHandle(ctx.Work.SessionId);
        if (existingHandle is not null)
        {
            if (ctx.SessionIngressToken is not null && ctx.SessionIngressToken != existingHandle.AccessToken)
            {
                await existingHandle.UpdateAccessTokenAsync(ctx.SessionIngressToken, ct).ConfigureAwait(false);
                _logger?.LogDebug("BridgeMain: updated token for existing session {SessionId}", ctx.Work.SessionId);
            }
            if (ctx.SessionIngressToken is not null)
            {
                ctx.Tracker.Sessions.UpdateIngressToken(ctx.Work.SessionId, ctx.SessionIngressToken);
            }
            ctx.ShortCircuited = true;
            return;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
