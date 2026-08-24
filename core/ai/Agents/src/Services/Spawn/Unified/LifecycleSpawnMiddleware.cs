namespace Core.Agents;

/// <summary>
/// 生命周期 Spawn 中间件 — 合并路径 A 的 ContextSetup Spawn 调用 + 路径 B 的 SpawnCoordLifecycle
/// 主代理 no-op（Agent 已由调用方预创建）
/// 路径 A：用 ResolvedSubOptions 调用 Spawn，并设置 ParentAgentId/SessionId
/// 路径 B：用 SubOptions 调用 Spawn
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware), ServiceLifetime.Singleton)]
public sealed partial class LifecycleSpawnMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public LifecycleSpawnMiddleware(IAgentLifecycleManager lifecycleManager, ISubAgentContextAccessor subAgentContextAccessor, ILogger<LifecycleSpawnMiddleware>? logger = null)
    {
        _lifecycleManager = lifecycleManager;
        _subAgentContextAccessor = subAgentContextAccessor;
        _logger = logger;
    }
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ILogger<LifecycleSpawnMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (context.Agent is not null)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var subOptions = context.ResolvedSubOptions ?? context.SubOptions;
        var parentSessionId = context.ParentSessionId ?? _subAgentContextAccessor.Current?.SessionId;
        var agent = await _lifecycleManager.SpawnSubAgentAsync(context.Task, subOptions, context.CancellationToken, parentSessionId).ConfigureAwait(false);
        context.Agent = agent;

        if (context.SpawnOptions is not null)
        {
            var concreteAgent = (AgentBase)agent;
            if (concreteAgent.Context is not null)
            {
                concreteAgent.Context.ParentAgentId = _subAgentContextAccessor.Current?.AgentId;
                concreteAgent.Context.SessionId = parentSessionId ?? concreteAgent.SessionId.UniqueId;
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
