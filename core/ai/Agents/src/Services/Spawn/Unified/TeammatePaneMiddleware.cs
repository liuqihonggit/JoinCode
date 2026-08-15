namespace Core.Agents.Unified;

/// <summary>
/// Teammate Pane 创建中间件 — 创建 Teammate UI Pane
/// 合并自路径 B 的 SpawnCoordTeammatePaneMiddleware
/// 主代理 no-op
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware))]
public sealed partial class TeammatePaneMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public TeammatePaneMiddleware(ISubAgentContextAccessor subAgentContextAccessor, ILogger<TeammatePaneMiddleware> logger, ITeammateLayoutManager? layoutManager = null)
    {
        _subAgentContextAccessor = subAgentContextAccessor;
        _logger = logger;
        _layoutManager = layoutManager;
    }
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly ITeammateLayoutManager? _layoutManager;
    [Inject] private readonly ILogger<TeammatePaneMiddleware> _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (!context.IsMainAgent && _layoutManager is not null && context.Agent is not null)
        {
            try
            {
                var agentType = _subAgentContextAccessor.Current?.Variant?.ToValue() ?? _subAgentContextAccessor.Current?.Role.ToValue() ?? "agent";
                var command = $"# Agent: {context.Task}";
                await _layoutManager.CreateTeammatePaneAsync(context.AgentId, agentType, command, context.CancellationToken).ConfigureAwait(false);
                context.TeammatePaneCreated = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TeammatePane] 创建 Teammate {AgentId} Pane 失败", context.AgentId);
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
