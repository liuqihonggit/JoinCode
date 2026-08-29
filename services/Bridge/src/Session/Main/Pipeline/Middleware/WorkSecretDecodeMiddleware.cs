namespace Core.Bridge;


[Register(typeof(IHandleWorkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorkSecretDecodeMiddleware : ServiceEntity, IHandleWorkMiddleware
{

    public WorkSecretDecodeMiddleware(ILogger<WorkSecretDecodeMiddleware>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<WorkSecretDecodeMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(ctx.Work.Secret))
        {
            try
            {
                ctx.Secret = BridgeWorkSecretDecoder.DecodeWorkSecret(ctx.Work.Secret);
                _logger?.LogDebug("BridgeMain: decoded work secret for WorkId={WorkId}, useCodeSessions={UseCcrV2}",
                    ctx.Work.WorkId, ctx.Secret.UseCodeSessions);
            }
            catch (Exception ex)
            {
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
