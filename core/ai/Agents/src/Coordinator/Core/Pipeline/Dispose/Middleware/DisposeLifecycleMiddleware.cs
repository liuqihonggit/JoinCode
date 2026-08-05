namespace Core.Agents.Coordinator;

[Register(typeof(IAgentDisposeMiddleware))]
public sealed partial class DisposeLifecycleMiddleware : ServiceEntity, IAgentDisposeMiddleware
{

    public DisposeLifecycleMiddleware(IAgentLifecycleManager lifecycleManager, ILogger<DisposeLifecycleMiddleware> logger)
    {
        _lifecycleManager = lifecycleManager;
        _logger = logger;
    }
    [Inject] private readonly IAgentLifecycleManager _lifecycleManager;
    [Inject] private readonly ILogger<DisposeLifecycleMiddleware> _logger;

    public async Task InvokeAsync(AgentDisposeContext ctx, MiddlewareDelegate<AgentDisposeContext> next, CancellationToken ct)
    {
        await _lifecycleManager.DisposeAgentAsync(ctx.AgentId, ctx.CancellationToken).ConfigureAwait(false);
        ctx.LifecycleDisposed = true;

        await next(ctx, ct).ConfigureAwait(false);
    }
}
