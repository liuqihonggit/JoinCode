namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 管道操作接口 — 供 Worktree 中间件使用的低级 Git 操作
/// 与高层 IAgentWorktreeService 分离，遵循接口隔离原则
/// </summary>
public interface IWorktreePipelineOperations
{
    Task SaveSessionAsync(AgentWorktreeSession session);
    Task<string?> GetCurrentBranchAsync(string gitRoot);
    Task<string?> GetHeadCommitShaAsync(string gitRoot);
    Task<bool> IsValidWorktreeAsync(string worktreePath, string gitRoot);
    Task<GitCommandResult> ExecuteGitCommandAsync(string workingDirectory, string arguments, CancellationToken cancellationToken = default);
    Task<bool> HasLocalBranchAsync(string gitRoot, string branchName, CancellationToken cancellationToken);
    Task<bool> ApplySparseCheckoutAsync(string worktreePath, IReadOnlyList<string> sparsePaths, CancellationToken cancellationToken);
}
