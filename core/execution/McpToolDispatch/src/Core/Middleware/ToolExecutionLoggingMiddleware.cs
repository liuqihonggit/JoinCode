namespace McpToolRegistry;

[Register]
public sealed partial class ToolExecutionLoggingMiddleware : IToolExecutionMiddleware
{
    [Inject] private readonly ILogger<ToolExecutionLoggingMiddleware> _logger = null!;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct)
    {
        try
        {
            await next(context, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} middleware error", context.ToolName);
            throw;
        }
    }
}
