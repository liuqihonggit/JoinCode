namespace Core.Agents.Coordinator;

/// <summary>
/// 生命周期释放中间件 — 在 Agent 释放管道中调用生命周期管理器释放 Agent 资源
/// </summary>
[Register(typeof(IAgentDisposeMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DisposeLifecycleMiddleware : ServiceEntity, IAgentDisposeMiddleware
{

    /// <summary>
    /// 构造生命周期释放中间件实例
    /// </summary>
    /// <param name="lifecycleManager">Agent 生命周期管理器</param>
    /// <param name="logger">日志记录器</param>
    public DisposeLifecycleMiddleware(IAgentLifecycleManager lifecycleManager, ILogger<DisposeLifecycleMiddleware> logger)
    {
        _lifecycleManager = lifecycleManager;
        _logger = logger;
    }
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly ILogger<DisposeLifecycleMiddleware> _logger;

    /// <summary>
    /// 执行中间件逻辑：释放指定 Agent 的生命周期资源后继续管道
    /// </summary>
    /// <param name="ctx">Agent 释放上下文</param>
    /// <param name="next">管道下一步委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(AgentDisposeContext ctx, MiddlewareDelegate<AgentDisposeContext> next, CancellationToken ct)
    {
        await _lifecycleManager.DisposeAgentAsync(ctx.AgentId, ctx.CancellationToken).ConfigureAwait(false);
        ctx.LifecycleDisposed = true;

        await next(ctx, ct).ConfigureAwait(false);
    }
}
