namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IShutdownMiddleware))]
public sealed partial class ShutdownDeregisterMiddleware : IShutdownMiddleware
{
    [Inject] private readonly ILogger<ShutdownDeregisterMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct)
    {
        if (!ctx.IsResuming && ctx.EnvironmentId is not null)
        {
            try
            {
                await ctx.ApiClient!.DeregisterEnvironmentAsync(
                    ctx.EnvironmentId, CancellationToken.None).ConfigureAwait(false);
                _logger?.LogInformation("BridgeMain: environment deregistered");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BridgeMain: deregister failed (non-fatal)");
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
