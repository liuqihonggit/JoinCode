namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IHandleWorkMiddleware))]
public sealed partial class WorkHealthcheckMiddleware : IHandleWorkMiddleware
{
    [Inject] private readonly ILogger<WorkHealthcheckMiddleware>? _logger;
    [Inject] private readonly BridgeApiClient _apiClient;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

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

        if (ctx.ActiveSessions!.TryGetValue(ctx.Work.SessionId, out var existingHandle))
        {
            if (ctx.SessionIngressToken is not null && ctx.SessionIngressToken != existingHandle.AccessToken)
            {
                await existingHandle.UpdateAccessTokenAsync(ctx.SessionIngressToken, ct).ConfigureAwait(false);
                _logger?.LogDebug("BridgeMain: updated token for existing session {SessionId}", ctx.Work.SessionId);
            }
            if (ctx.SessionIngressToken is not null)
            {
                ctx.SessionIngressTokens![ctx.Work.SessionId] = ctx.SessionIngressToken;
            }
            ctx.ShortCircuited = true;
            return;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
