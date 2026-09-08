namespace Core.Agents.Coordinator;

[Register(typeof(IAgentDisposeMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DisposeWorktreeCleanupMiddleware : ServiceEntity, IAgentDisposeMiddleware
{

    public DisposeWorktreeCleanupMiddleware(IAgentWorktreeManager worktreeManager, ILogger<DisposeWorktreeCleanupMiddleware> logger)
    {
        _worktreeManager = worktreeManager;
        _logger = logger;
    }
    private readonly IAgentWorktreeManager _worktreeManager;
    private readonly ILogger<DisposeWorktreeCleanupMiddleware> _logger;

    public async Task InvokeAsync(AgentDisposeContext ctx, MiddlewareDelegate<AgentDisposeContext> next, CancellationToken ct)
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
            else if (!cleanupDetail.Kept && !string.IsNullOrEmpty(cleanupDetail.WorktreePath))
            {
                _logger.LogInformation("已释放git worktree,路径是:{WorktreePath} 已释放git分支:{BranchName}",
                    cleanupDetail.WorktreePath, cleanupDetail.BranchName ?? "unknown");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AgentCoordinator] 清理Agent {AgentId} Worktree时发生异常", ctx.AgentId);
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
