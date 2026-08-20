namespace McpToolRegistry;

/// <summary>
/// 工具修正 Hook 中间件 — Order=860 — 工具执行失败时调用 ToolFixHookRegistry 尝试自动修正
/// 集成位置：ToolHealthScoringMiddleware（Order=850）之后、PostToolUseHookMiddleware 之前
/// 触发条件：ToolHealthMonitor.ConsecutiveFailures 达到阈值（默认3次）
/// 修正方式：按优先级遍历 IToolFixHook，将修正建议注入到结果的 InjectedMessages 中
/// </summary>
[Register]
public sealed partial class ToolFixHookMiddleware : ServiceEntity, IToolExecutionMiddleware
{
    private readonly Core.Hooks.Execution.ToolFixHookRegistry _fixHookRegistry;
    private readonly ILogger<ToolFixHookMiddleware> _logger;

    public ToolFixHookMiddleware(
        Core.Hooks.Execution.ToolFixHookRegistry fixHookRegistry,
        ILogger<ToolFixHookMiddleware> logger)
    {
        _fixHookRegistry = fixHookRegistry;
        _logger = logger;
    }

    public ErrorBehavior OnError => ErrorBehavior.Continue;

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
