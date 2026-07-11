namespace Core.Agents.Worktree;

/// <summary>
/// Worktree Git 信息获取中间件 — 获取当前分支和 HEAD commit SHA
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware))]
public sealed partial class WorktreeGitInfoMiddleware : IWorktreeCreateMiddleware
{
    [Inject] private readonly Lazy<IWorktreePipelineOperations> _worktreeService;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        var gitRoot = context.GitRoot;

        context.OriginalBranch = await _worktreeService.Value.GetCurrentBranchAsync(gitRoot).ConfigureAwait(false);
        context.BaseCommitSha = await _worktreeService.Value.GetHeadCommitShaAsync(gitRoot).ConfigureAwait(false);

        await next(context, ct).ConfigureAwait(false);
    }
}
