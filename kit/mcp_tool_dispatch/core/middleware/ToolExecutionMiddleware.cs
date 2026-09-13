
namespace McpToolRegistry;

/// <summary>
/// 工具执行终端中间件 — Order=900 — 实际调用工具处理器执行
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ToolExecutionMiddleware : ServiceEntity, IToolExecutionMiddleware
{

    private readonly ILogger<ToolExecutionMiddleware> _logger;

    /// <summary>
    /// 构造函数 — 注入日志记录器
    /// </summary>
    /// <param name="logger">日志记录器实例</param>
    public ToolExecutionMiddleware(ILogger<ToolExecutionMiddleware> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 实际调用工具处理器执行；若 Handler 为空则返回未找到错误，执行成功设置遥测状态 Ok，异常被捕获并转为错误结果
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
        try
        {
            var result = await context.Handler.ExecuteAsync(
                context.Arguments, ct, context.OnProgress).ConfigureAwait(false);
            _logger.LogInformation(L.T(StringKey.ToolExecSuccessLog, context.ToolName));
            context.Span?.SetStatus(TelemetryStatusCode.Ok);
            context.Result = result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, L.T(StringKey.ToolExecFailedLog, context.ToolName));
            context.Result = new ToolResult
            {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = $"{ex.GetType().Name}: {ex.Message}" }],
                IsError = true
            };
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
