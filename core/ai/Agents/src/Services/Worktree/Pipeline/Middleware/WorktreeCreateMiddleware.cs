namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 创建中间件 — git worktree add + 可选稀疏检出（失败回滚）
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorktreeCreateMiddleware : ServiceEntity, IWorktreeCreateMiddleware
{

    public WorktreeCreateMiddleware(Lazy<IWorktreePipelineOperations> worktreeService, IFileOperationService fs, IClockService clock, ILogger<WorktreeCreateMiddleware>? logger = null)
    {
        _worktreeService = worktreeService;
        _fs = fs;
        _clock = clock;
        _logger = logger;
    }
    private readonly Lazy<IWorktreePipelineOperations> _worktreeService;
    private readonly IFileOperationService _fs;
    private readonly ILogger<WorktreeCreateMiddleware>? _logger;
    private readonly IClockService _clock;


    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        var opts = context.Options ?? new WorktreeOptions();
        var gitRoot = context.GitRoot;
        var worktreePath = context.WorktreePath;
        var branchName = context.BranchName;

        context.CreationStartTime = _clock.GetUtcNow();
        _logger?.LogInformation("创建新 worktree: {WorktreePath}, Agent: {AgentId}", worktreePath, context.AgentId);

        var hasLocalRef = await _worktreeService.Value.HasLocalBranchAsync(gitRoot, branchName, ct).ConfigureAwait(false);
        var hasSparsePaths = opts.SparsePaths?.Count > 0;
        var baseRef = context.BaseBranch ?? "HEAD";

        var worktreeAddArgs = hasLocalRef
            ? $"worktree add \"{worktreePath}\" -B {branchName}"
            : hasSparsePaths
                ? $"worktree add --no-checkout -B {branchName} \"{worktreePath}\" {baseRef}"
                : $"worktree add -B {branchName} \"{worktreePath}\" {baseRef}";

        var createResult = await _worktreeService.Value.ExecuteGitCommandAsync(gitRoot, worktreeAddArgs, ct).ConfigureAwait(false);

        if (!createResult.Success)
        {
            context.Fail($"创建 worktree 失败: {createResult.Error}");
            return;
        }

        if (hasSparsePaths)
        {
            var sparseResult = await _worktreeService.Value.ApplySparseCheckoutAsync(worktreePath, opts.SparsePaths!, ct).ConfigureAwait(false);
            if (!sparseResult)
            {
                _logger?.LogWarning("应用稀疏检出失败，回滚 worktree: {WorktreePath}", worktreePath);
                await _worktreeService.Value.ExecuteGitCommandAsync(gitRoot, $"worktree remove --force \"{worktreePath}\"", ct).ConfigureAwait(false);
                await _worktreeService.Value.ExecuteGitCommandAsync(gitRoot, $"branch -D {branchName}", ct).ConfigureAwait(false);
                context.Fail("稀疏检出失败，已回滚 worktree");
                return;
            }

            var checkoutResult = await _worktreeService.Value.ExecuteGitCommandAsync(worktreePath, "checkout HEAD", ct).ConfigureAwait(false);
            if (!checkoutResult.Success)
            {
                _logger?.LogWarning("稀疏检出 checkout HEAD 失败，回滚 worktree: {WorktreePath}", worktreePath);
                await _worktreeService.Value.ExecuteGitCommandAsync(gitRoot, $"worktree remove --force \"{worktreePath}\"", ct).ConfigureAwait(false);
                await _worktreeService.Value.ExecuteGitCommandAsync(gitRoot, $"branch -D {branchName}", ct).ConfigureAwait(false);
                context.Fail($"稀疏检出 checkout HEAD 失败: {checkoutResult.Error}，已回滚 worktree");
                return;
            }
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
