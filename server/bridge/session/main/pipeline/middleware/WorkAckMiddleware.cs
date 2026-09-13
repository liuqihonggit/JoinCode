namespace Core.Bridge;


/// <summary>
/// 工作 ACK 中间件 — 向 API 客户端确认工作项已接收，支持 ingress token 与无 token 两种模式
/// </summary>
[Register(typeof(IHandleWorkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorkAckMiddleware : ServiceEntity, IHandleWorkMiddleware
{

    /// <summary>
    /// 构造 WorkAck 中间件
    /// </summary>
    /// <param name="apiClient">Bridge API 客户端</param>
    /// <param name="logger">可选日志记录器</param>
    public WorkAckMiddleware(BridgeApiClient apiClient, ILogger<WorkAckMiddleware>? logger = null)
    {
        _apiClient = apiClient;
        _logger = logger;
    }
    private readonly ILogger<WorkAckMiddleware>? _logger;
    private readonly BridgeApiClient _apiClient;

    /// <summary>错误行为策略 — 继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行 ACK 中间件 — 发送工作确认后调用下一个委托；无 ingress token 失败时短路
    /// </summary>
    /// <param name="ctx">工作处理上下文</param>
    /// <param name="next">管道下一个委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct)
    {
        if (ctx.SessionIngressToken is not null)
        {
            await _apiClient.AcknowledgeWorkAsync(
                ctx.EnvironmentId ?? "", ctx.Work.WorkId, ctx.SessionIngressToken, ct).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await _apiClient.AcknowledgeWorkAsync(
                    ctx.EnvironmentId ?? "", ctx.Work.WorkId, ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BridgeMain: ACK failed for work {WorkId}", ctx.Work.WorkId);
                ctx.ShortCircuited = true;
                return;
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
