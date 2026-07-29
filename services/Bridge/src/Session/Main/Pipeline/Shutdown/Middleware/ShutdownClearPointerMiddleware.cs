namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IShutdownMiddleware))]
public sealed partial class ShutdownClearPointerMiddleware : IShutdownMiddleware
{
    [Inject] private readonly ILogger<ShutdownClearPointerMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct)
    {
        if (!ctx.IsResuming && ctx.SpawnMode == BridgeSpawnMode.SingleSession)
        {
            try
            {
                var pointerDir = ctx.ResumePointerDir ?? ctx.WorkingDirectory;
                if (pointerDir is not null)
                {
                    await (ctx.PointerService ?? throw new InvalidOperationException("PointerService not available")).ClearAsync(pointerDir).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "BridgeMain: pointer clear failed (non-fatal)");
            }
        }

        ctx.PointerRefreshTimer?.Dispose();

        _logger?.LogInformation("BridgeMain: shutdown complete");

        await next(ctx, ct).ConfigureAwait(false);
    }
}
