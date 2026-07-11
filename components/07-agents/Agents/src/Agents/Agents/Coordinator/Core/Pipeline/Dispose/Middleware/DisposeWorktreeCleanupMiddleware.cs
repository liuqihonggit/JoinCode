namespace Core.Agents.Coordinator;

[Register(typeof(IAgentDisposeMiddleware))]
public sealed partial class DisposeWorktreeCleanupMiddleware : IAgentDisposeMiddleware
{
    [Inject] private readonly IAgentWorktreeManager _worktreeManager;
    [Inject] private readonly ILogger<DisposeWorktreeCleanupMiddleware> _logger;

    public async Task InvokeAsync(AgentDisposeContext ctx, MiddlewareDelegate<AgentDisposeContext> next, CancellationToken ct)
    {
        if (_worktreeManager.IsWorktreeIsolationEnabled)
        {
            try
            {
                var cleanupDetail = await _worktreeManager.CleanupWorktreeAsync(ctx.AgentId, ctx.CancellationToken).ConfigureAwait(false);
                ctx.WorktreeCleanupResult = cleanupDetail;
                if (cleanupDetail.Kept && cleanupDetail.WorktreePath is not null)
                {
                    _logger.LogInformation("[AgentCoordinator] Agent {AgentId} worktree kept (reason: {Reason}): {WorktreePath}, branch: {BranchName}",
                        ctx.AgentId, cleanupDetail.Reason, cleanupDetail.WorktreePath, cleanupDetail.BranchName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AgentCoordinator] 清理Agent {AgentId} Worktree时发生异常", ctx.AgentId);
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
