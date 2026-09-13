namespace Core.Agents.Coordinator;

/// <summary>
/// Worktree 清理中间件 — 在 Agent 释放管道中清理该 Agent 关联的 Git Worktree
/// </summary>
[Register(typeof(IAgentDisposeMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DisposeWorktreeCleanupMiddleware : ServiceEntity, IAgentDisposeMiddleware
{

    /// <summary>
    /// 构造 Worktree 清理中间件实例
    /// </summary>
    /// <param name="worktreeManager">Agent Worktree 管理器</param>
    /// <param name="logger">日志记录器</param>
    public DisposeWorktreeCleanupMiddleware(IAgentWorktreeManager worktreeManager, ILogger<DisposeWorktreeCleanupMiddleware> logger)
    {
        _worktreeManager = worktreeManager;
        _logger = logger;
    }
    private readonly IAgentWorktreeManager _worktreeManager;
    private readonly ILogger<DisposeWorktreeCleanupMiddleware> _logger;

    /// <summary>
    /// 执行中间件逻辑：清理指定 Agent 的 Worktree 后继续管道
    /// </summary>
    /// <param name="ctx">Agent 释放上下文</param>
    /// <param name="next">管道下一步委托</param>
    /// <param name="ct">取消令牌</param>
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
