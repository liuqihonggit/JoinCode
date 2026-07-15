namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IHandleWorkMiddleware))]
public sealed partial class WorkCcrV2RegisterMiddleware : IHandleWorkMiddleware
{
    [Inject] private readonly ILogger<WorkCcrV2RegisterMiddleware>? _logger;
    [Inject] private readonly BridgeApiClient _apiClient;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct)
    {
        var forceCcrV2 = Environment.GetEnvironmentVariable("CLAUDE_BRIDGE_USE_CCR_V2") is "1" or "true";

        if ((ctx.Secret?.UseCodeSessions == true || forceCcrV2) && ctx.SecretApiBaseUrl is not null && ctx.SessionIngressToken is not null)
        {
            ctx.SdkUrl = BridgeWorkSecretDecoder.BuildCCRv2SdkUrl(ctx.SecretApiBaseUrl, ctx.Work.SessionId);

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    ctx.WorkerEpoch = (int)await BridgeWorkSecretDecoder.RegisterWorkerAsync(
                        ctx.SdkUrl, ctx.SessionIngressToken, _apiClient.HttpClient, ct).ConfigureAwait(false);
                    ctx.UseCcrV2 = true;
                    _logger?.LogInformation(
                        "BridgeMain: CCR v2 registered worker, SessionId={SessionId}, epoch={Epoch}, attempt={Attempt}",
                        ctx.Work.SessionId, ctx.WorkerEpoch, attempt);
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt < 2)
                    {
                        _logger?.LogDebug(ex,
                            "BridgeMain: CCR v2 registerWorker attempt {Attempt} failed, retrying", attempt);
                        await Task.Delay(2000, ct).ConfigureAwait(false);
                        continue;
                    }

                    _logger?.LogError(ex,
                        "BridgeMain: CCR v2 worker registration failed for session {SessionId}", ctx.Work.SessionId);
                    ctx.CompletedWorkIds.Add(ctx.Work.WorkId);
                    if (ctx.TrackCleanup is not null && ctx.StopWorkAsync is not null)
                    {
                        ctx.TrackCleanup(ctx.StopWorkAsync(ctx.Work.WorkId, ct));
                    }
                    ctx.CapacityWake?.Invoke();
                    ctx.ShortCircuited = true;
                    return;
                }
            }
        }
        else
        {
            var ingressUrl = ctx.SecretApiBaseUrl ?? ctx.Config.SessionIngressUrl;
            ctx.SdkUrl = BridgeWorkSecretDecoder.BuildSdkUrl(ingressUrl, ctx.Work.SessionId);
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
