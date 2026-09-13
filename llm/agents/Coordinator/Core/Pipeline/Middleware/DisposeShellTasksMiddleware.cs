namespace Core.Agents.Coordinator;

/// <summary>
/// Shell 任务释放中间件 — 在 Agent 释放管道中取消该 Agent 启动的所有后台 Shell 任务
/// </summary>
[Register(typeof(IAgentDisposeMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DisposeShellTasksMiddleware : ServiceEntity, IAgentDisposeMiddleware
{

    /// <summary>
    /// 构造 Shell 任务释放中间件实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="actuatorRegistry">可选系统执行器注册表，缺省时跳过任务取消</param>
    public DisposeShellTasksMiddleware(ILogger<DisposeShellTasksMiddleware> logger, ISystemActuatorRegistry? actuatorRegistry = null)
    {
        _logger = logger;
        _actuatorRegistry = actuatorRegistry;
    }
    private readonly ISystemActuatorRegistry? _actuatorRegistry;
    private readonly ILogger<DisposeShellTasksMiddleware> _logger;

    /// <summary>
    /// 执行中间件逻辑：取消指定 Agent 的后台 Shell 任务后继续管道
    /// </summary>
    /// <param name="ctx">Agent 释放上下文</param>
    /// <param name="next">管道下一步委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(AgentDisposeContext ctx, MiddlewareDelegate<AgentDisposeContext> next, CancellationToken ct)
    {
        if (_actuatorRegistry is not null)
        {
            try
            {
                var cancelledCount = await _actuatorRegistry.CancelTasksForAgentAsync(ctx.AgentId, ctx.CancellationToken).ConfigureAwait(false);
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
