namespace McpToolRegistry;

[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ToolExecutionLoggingMiddleware : ServiceEntity, IToolExecutionMiddleware
{
    private readonly ILogger<ToolExecutionLoggingMiddleware> _logger;

    public ToolExecutionLoggingMiddleware(ILogger<ToolExecutionLoggingMiddleware> logger)
    {
        _logger = logger;
    }

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
