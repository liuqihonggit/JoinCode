
namespace McpToolRegistry;

/// <summary>
/// 工具执行终端中间件 — Order=900 — 实际调用工具处理器执行
/// </summary>
[Register]
public sealed partial class ToolExecutionMiddleware : IToolExecutionMiddleware
{

    [Inject] private readonly ILogger<ToolExecutionMiddleware> _logger;

    public ToolExecutionMiddleware(ILogger<ToolExecutionMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (context.Handler is null)
        {
            context.Result = new ToolResult
            {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = $"Tool '{context.ToolName}' handler not found." }],
                IsError = true
            };
            return;
        }

        _logger.LogDebug(L.T(StringKey.ToolExecStartLog, context.ToolName));
        var result = await context.Handler.ExecuteAsync(
            context.Arguments, ct, context.OnProgress).ConfigureAwait(false);
        _logger.LogInformation(L.T(StringKey.ToolExecSuccessLog, context.ToolName));
        context.Span?.SetStatus(TelemetryStatusCode.Ok);
        context.Result = result;

        await next(context, ct).ConfigureAwait(false);
    }
}
