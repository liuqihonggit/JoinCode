namespace McpToolRegistry;

/// <summary>
/// 工具修正 Hook 中间件 — Order=860 — 工具执行失败时调用 ToolFixHookRegistry 尝试自动修正
/// 集成位置：ToolHealthScoringMiddleware（Order=850）之后、PostToolUseHookMiddleware 之前
/// 触发条件：ToolHealthMonitor.ConsecutiveFailures 达到阈值（默认3次）
/// 修正方式：按优先级遍历 IToolFixHook，将修正建议注入到结果的 InjectedMessages 中
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ToolFixHookMiddleware : ServiceEntity, IToolExecutionMiddleware
{
    private readonly Core.Hooks.Execution.ToolFixHookRegistry _fixHookRegistry;
    private readonly ILogger<ToolFixHookMiddleware> _logger;

    /// <summary>
    /// 构造函数 — 注入修正 Hook 注册表和日志记录器
    /// </summary>
    /// <param name="fixHookRegistry">工具修正 Hook 注册表，提供按优先级遍历的自动修正能力</param>
    /// <param name="logger">日志记录器实例</param>
    public ToolFixHookMiddleware(
        Core.Hooks.Execution.ToolFixHookRegistry fixHookRegistry,
        ILogger<ToolFixHookMiddleware> logger)
    {
        _fixHookRegistry = fixHookRegistry;
        _logger = logger;
    }

    /// <summary>
    /// 错误处理行为 — Continue 表示修正失败不中断管道
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 先调用下一层中间件完成工具执行；若结果为错误则尝试调用修正 Hook 自动修正，并将修正建议注入到结果的 InjectedMessages 中
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <param name="next">下一层中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct)
    {
        await next(context, ct).ConfigureAwait(false);

        if (context.Result is null || !context.Result.IsError) return;

        var errorMsg = context.Result.GetFirstText();
        if (string.IsNullOrEmpty(errorMsg)) return;

        try
        {
            var fixResult = await _fixHookRegistry.TryFixAsync(
                context.ToolName,
                new InvalidOperationException(errorMsg),
                ct).ConfigureAwait(false);

            if (!fixResult.Success) return;

            _logger.LogInformation("工具 {ToolName} 自动修正: {Description}", context.ToolName, fixResult.Description);

            var fixMessage = new JoinCode.Abstractions.LLM.Chat.ApiMessage(
                JoinCode.Abstractions.LLM.Chat.MessageRole.User,
                $"[系统提示] 工具 '{context.ToolName}' 触发自动修正: {fixResult.Description}");

            context.Result = context.Result with
            {
                InjectedMessages = [.. (context.Result.InjectedMessages ?? []), fixMessage]
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "工具 {ToolName} 修正 Hook 执行失败", context.ToolName);
        }
    }
}
