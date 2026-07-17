namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IShutdownMiddleware))]
public sealed partial class ShutdownCancelLoopMiddleware : IShutdownMiddleware
{
    [Inject] private readonly ILogger<ShutdownCancelLoopMiddleware>? _logger;


    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct)
    {
        _logger?.LogInformation("BridgeMain: shutting down...");

        ctx.UnregisterKeyboardListener?.Invoke();

        await (ctx.LoopCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);

        if (ctx.LoopTask is not null)
        {
            try
            {
                await ctx.LoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
