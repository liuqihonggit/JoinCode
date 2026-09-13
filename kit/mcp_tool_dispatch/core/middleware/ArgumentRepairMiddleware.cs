
namespace McpToolRegistry;

/// <summary>
/// 参数修复中间件 — Order=100 — 修复工具调用参数中的常见问题
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ArgumentRepairMiddleware : ServiceEntity, IToolExecutionMiddleware
{

    private readonly ILogger<ArgumentRepairMiddleware> _logger;

    /// <summary>
    /// 构造函数 — 注入日志记录器
    /// </summary>
    /// <param name="logger">日志记录器实例</param>
    public ArgumentRepairMiddleware(ILogger<ArgumentRepairMiddleware> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 调用 LlmJsonHelper.RepairArguments 修复工具调用参数中的常见问题（如类型不匹配、字段缺失），修复后写回上下文，再调用下一层中间件
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <param name="next">下一层中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct)
    {
        if (context.Handler is not null)
        {
            var argRepair = LlmJsonHelper.RepairArguments(
                context.ToolName, context.Arguments, context.Handler.InputSchema, _logger);
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
