namespace McpToolRegistry;

/// <summary>
/// 工具执行日志中间件 — 捕获工具执行管道中的异常并记录日志，异常继续向外层传播
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ToolExecutionLoggingMiddleware : ServiceEntity, IToolExecutionMiddleware
{
    private readonly ILogger<ToolExecutionLoggingMiddleware> _logger;

    /// <summary>
    /// 构造函数 — 注入日志记录器
    /// </summary>
    /// <param name="logger">日志记录器实例</param>
    public ToolExecutionLoggingMiddleware(ILogger<ToolExecutionLoggingMiddleware> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 错误处理行为 — Continue 表示捕获并记录异常后继续向外层传播
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 调用下一层中间件，捕获异常并记录错误日志后重新抛出
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <param name="next">下一层中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
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
