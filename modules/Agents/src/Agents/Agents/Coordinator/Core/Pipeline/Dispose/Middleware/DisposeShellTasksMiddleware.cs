namespace Core.Agents.Coordinator;

[Register(typeof(IAgentDisposeMiddleware))]
public sealed partial class DisposeShellTasksMiddleware : IAgentDisposeMiddleware
{
    [Inject] private readonly IShellBackgroundTaskService? _shellBackgroundTaskService;
    [Inject] private readonly ILogger<DisposeShellTasksMiddleware> _logger;

    public async Task InvokeAsync(AgentDisposeContext ctx, MiddlewareDelegate<AgentDisposeContext> next, CancellationToken ct)
    {
        if (_shellBackgroundTaskService is not null)
        {
            try
            {
                var cancelledCount = await _shellBackgroundTaskService.CancelTasksForAgentAsync(ctx.AgentId, ctx.CancellationToken).ConfigureAwait(false);
                ctx.CancelledShellTaskCount = cancelledCount;
                if (cancelledCount > 0)
                {
                    _logger.LogInformation("[AgentCoordinator] 已取消Agent {AgentId} 的 {Count} 个后台Shell任务", ctx.AgentId, cancelledCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AgentCoordinator] 清理Agent {AgentId} 后台Shell任务时发生异常", ctx.AgentId);
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
