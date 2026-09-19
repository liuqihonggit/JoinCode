namespace Core.Bridge;


/// <summary>
/// 工作密钥解码中间件 — 解码 Work.Secret 填充到上下文
/// </summary>
[Register(typeof(IHandleWorkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorkSecretDecodeMiddleware : ServiceEntity, IHandleWorkMiddleware {

    /// <summary>
    /// 构造工作密钥解码中间件
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public WorkSecretDecodeMiddleware(ILogger<WorkSecretDecodeMiddleware>? logger = null) {
        _logger = logger;
    }
    private readonly ILogger<WorkSecretDecodeMiddleware>? _logger;

    /// <summary>错误行为策略</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行中间件 — 解码工作密钥并填充到上下文
    /// </summary>
    /// <param name="ctx">工作处理上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct) {
        if (!string.IsNullOrEmpty(ctx.Work.Secret)) {
            try {
                ctx.Secret = BridgeWorkSecretDecoder.DecodeWorkSecret(ctx.Work.Secret);
                _logger?.LogDebug("BridgeMain: decoded work secret for WorkId={WorkId}, useCodeSessions={UseCcrV2}",
                    ctx.Work.WorkId, ctx.Secret.UseCodeSessions);
            } catch (Exception ex) {
                _logger?.LogError(ex, "BridgeMain: failed to decode work secret for WorkId={WorkId}", ctx.Work.WorkId);
                ctx.TelemetryCount?.Invoke("tengu_bridge_work_secret_failed", null);
                ctx.FailWork(ct);
                return;
            }
        }

        ctx.SessionIngressToken = ctx.Secret?.SessionIngressToken ?? ctx.Work.SessionIngressToken;
        ctx.SecretApiBaseUrl = ctx.Secret?.ApiBaseUrl ?? ctx.Work.ApiBaseUrl;

        await next(ctx, ct).ConfigureAwait(false);
    }
}