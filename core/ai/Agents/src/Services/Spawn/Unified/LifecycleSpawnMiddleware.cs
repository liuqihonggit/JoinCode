namespace Core.Agents.Unified;

/// <summary>
/// 生命周期 Spawn 中间件 — 合并路径 A 的 ContextSetup Spawn 调用 + 路径 B 的 SpawnCoordLifecycle
/// 主代理 no-op（Agent 已由调用方预创建）
/// 路径 A：用 ResolvedSubOptions 调用 Spawn，并设置 ParentAgentId/SessionId
/// 路径 B：用 SubOptions 调用 Spawn
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware))]
public sealed partial class LifecycleSpawnMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public LifecycleSpawnMiddleware(IAgentLifecycleManager lifecycleManager, ISubAgentContextAccessor subAgentContextAccessor, ILogger<LifecycleSpawnMiddleware>? logger = null)
    {
        _lifecycleManager = lifecycleManager;
        _subAgentContextAccessor = subAgentContextAccessor;
        _logger = logger;
    }
    [Inject] private readonly IAgentLifecycleManager _lifecycleManager;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly ILogger<LifecycleSpawnMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (context.Agent is not null)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var subOptions = context.ResolvedSubOptions ?? context.SubOptions;
        var agent = await _lifecycleManager.SpawnSubAgentAsync(context.Task, subOptions, context.CancellationToken).ConfigureAwait(false);
        context.Agent = agent;

        if (context.SpawnOptions is not null)
        {
            var concreteAgent = (AgentBase)agent;
            if (concreteAgent.Context is not null)
            {
                concreteAgent.Context.ParentAgentId = _subAgentContextAccessor.Current?.AgentId;
                concreteAgent.Context.SessionId = "default";
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
