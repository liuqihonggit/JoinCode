namespace Core.Agents.Coordinator;

[Register(typeof(IAgentSpawnCoordMiddleware))]
public sealed partial class SpawnCoordTeammatePaneMiddleware : IAgentSpawnCoordMiddleware
{
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly ITeammateLayoutManager? _layoutManager;
    [Inject] private readonly ILogger<SpawnCoordTeammatePaneMiddleware> _logger;

    public async Task InvokeAsync(AgentSpawnCoordContext ctx, MiddlewareDelegate<AgentSpawnCoordContext> next, CancellationToken ct)
    {
        if (_layoutManager is not null)
        {
            try
            {
                var agentType = _subAgentContextAccessor.Current?.AgentType ?? "agent";
                var command = $"# Agent: {ctx.Task}";
                await _layoutManager.CreateTeammatePaneAsync(ctx.AgentId, agentType, command, ctx.CancellationToken).ConfigureAwait(false);
                ctx.TeammatePaneCreated = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AgentCoordinator] 创建 Teammate {AgentId} Pane 失败", ctx.AgentId);
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
