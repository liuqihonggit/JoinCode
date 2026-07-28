namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IHandleWorkMiddleware))]
public sealed partial class WorkAckMiddleware : IHandleWorkMiddleware
{
    [Inject] private readonly ILogger<WorkAckMiddleware>? _logger;
    [Inject] private readonly BridgeApiClient _apiClient;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

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
