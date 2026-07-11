namespace Core.Agents.Coordinator;

[Register(typeof(IAgentDisposeMiddleware))]
public sealed partial class DisposePaneMiddleware : IAgentDisposeMiddleware
{
    [Inject] private readonly ITeammateLayoutManager? _layoutManager;
    [Inject] private readonly ILogger<DisposePaneMiddleware> _logger;

    public async Task InvokeAsync(AgentDisposeContext ctx, MiddlewareDelegate<AgentDisposeContext> next, CancellationToken ct)
    {
        if (_layoutManager is not null)
        {
            try
            {
                await _layoutManager.RemoveTeammatePaneAsync(ctx.AgentId, ctx.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AgentCoordinator] 移除 Teammate {AgentId} Pane 失败", ctx.AgentId);
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
