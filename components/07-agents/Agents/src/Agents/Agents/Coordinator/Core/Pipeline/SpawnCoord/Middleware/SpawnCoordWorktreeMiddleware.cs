namespace Core.Agents.Coordinator;

[Register(typeof(IAgentSpawnCoordMiddleware))]
public sealed partial class SpawnCoordWorktreeMiddleware : IAgentSpawnCoordMiddleware
{
    [Inject] private readonly IAgentWorktreeManager _worktreeManager;
    [Inject] private readonly IAgentLifecycleManager _lifecycleManager;
    [Inject] private readonly ILogger<SpawnCoordWorktreeMiddleware> _logger;


    public async Task InvokeAsync(AgentSpawnCoordContext ctx, MiddlewareDelegate<AgentSpawnCoordContext> next, CancellationToken ct)
    {
        if (_worktreeManager.IsWorktreeIsolationEnabled)
        {
            var worktreeCreated = await _worktreeManager.CreateWorktreeAsync(ctx.AgentId, ctx.CancellationToken).ConfigureAwait(false);
            if (!worktreeCreated)
            {
                await _lifecycleManager.DisposeAgentAsync(ctx.AgentId, ctx.CancellationToken).ConfigureAwait(false);
                _logger.LogError("[AgentCoordinator] 无法为Agent {AgentId} 创建Worktree", ctx.AgentId);
                throw new InvalidOperationException($"无法为Agent {ctx.AgentId} 创建Worktree");
            }
            ctx.WorktreeCreated = true;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
