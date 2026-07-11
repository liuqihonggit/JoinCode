namespace Core.Agents.Coordinator;

[Register(typeof(IAgentSpawnCoordMiddleware))]
public sealed partial class SpawnCoordLifecycleMiddleware : IAgentSpawnCoordMiddleware
{
    [Inject] private readonly IAgentLifecycleManager _lifecycleManager;
    [Inject] private readonly ILogger<SpawnCoordLifecycleMiddleware> _logger;

    public async Task InvokeAsync(AgentSpawnCoordContext ctx, MiddlewareDelegate<AgentSpawnCoordContext> next, CancellationToken ct)
    {
        var agent = await _lifecycleManager.SpawnSubAgentAsync(ctx.Task, ctx.Options, ctx.CancellationToken).ConfigureAwait(false);
        ctx.Agent = agent;

        await next(ctx, ct).ConfigureAwait(false);
    }
}
