
namespace McpToolRegistry;

/// <summary>
/// 参数修复中间件 — Order=100 — 修复工具调用参数中的常见问题
/// </summary>
[Register]
public sealed partial class ArgumentRepairMiddleware : IToolExecutionMiddleware
{

    [Inject] private readonly ILogger<ArgumentRepairMiddleware> _logger;

    public ArgumentRepairMiddleware(ILogger<ArgumentRepairMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (context.Handler is not null)
        {
            var argRepair = ToolCallRepairService.RepairArguments(
                context.ToolName, context.Arguments, context.Handler.InputSchema);
            if (argRepair.RepairHint is not null)
            {
                context.Arguments = argRepair.RepairedArguments;
                _logger.LogDebug("Tool {ToolName} arguments repaired: {Hint}",
                    context.ToolName, argRepair.RepairHint);
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
