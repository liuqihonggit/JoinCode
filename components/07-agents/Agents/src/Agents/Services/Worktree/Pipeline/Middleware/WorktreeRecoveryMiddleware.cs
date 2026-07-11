namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 恢复中间件 — 检查现有 worktree 是否可恢复，命中时短路
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware))]
public sealed partial class WorktreeRecoveryMiddleware : IWorktreeCreateMiddleware
{
    [Inject] private readonly IFileOperationService _fs;
    [Inject] private readonly ILogger<WorktreeRecoveryMiddleware>? _logger;
    [Inject] private readonly Lazy<IWorktreePipelineOperations> _worktreeService;
    [Inject] private readonly IClockService _clock;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        var worktreePath = AgentWorktreeSession.GenerateWorktreePath(context.GitRoot, context.AgentId);
        var branchName = AgentWorktreeSession.GenerateBranchName(context.AgentId);

        if (_fs.DirectoryExists(worktreePath) && await _worktreeService.Value.IsValidWorktreeAsync(worktreePath, context.GitRoot).ConfigureAwait(false))
        {
            _logger?.LogInformation("恢复现有 worktree: {WorktreePath}, Agent: {AgentId}", worktreePath, context.AgentId);

            var existingSession = new AgentWorktreeSession
            {
                AgentId = context.AgentId,
                OriginalCwd = context.OriginalCwd,
                WorktreePath = worktreePath,
                BranchName = branchName,
                GitRootPath = context.GitRoot,
                CreatedAt = _clock.GetUtcNow(),
                Existed = true
            };

            await _worktreeService.Value.SaveSessionAsync(existingSession).ConfigureAwait(false);

            context.IsRecovery = true;
            context.RecoveredSession = existingSession;
            context.WorktreePath = worktreePath;
            context.BranchName = branchName;
            context.Result = WorktreeCreateResult.SuccessResult(existingSession, true);
            return;
        }

        context.WorktreePath = worktreePath;
        context.BranchName = branchName;

        await next(context, ct).ConfigureAwait(false);
    }
}
