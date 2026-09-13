namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 管道操作接口 — 供 Worktree 中间件使用的低级 Git 操作
/// 与高层 IAgentWorktreeService 分离，遵循接口隔离原则
/// </summary>
public interface IWorktreePipelineOperations
{
    /// <summary>保存 worktree 会话</summary>
    Task SaveSessionAsync(AgentWorktreeSession session);
    /// <summary>获取当前分支名称</summary>
    Task<string?> GetCurrentBranchAsync(string gitRoot);
    /// <summary>获取 HEAD 提交 SHA</summary>
    Task<string?> GetHeadCommitShaAsync(string gitRoot);
    /// <summary>获取默认分支名称</summary>
    Task<string?> GetDefaultBranchAsync(string gitRoot);
    /// <summary>解析引用为提交 SHA</summary>
    Task<string?> ResolveRefAsync(string gitRoot, string refName);
    /// <summary>验证路径是否为有效 worktree</summary>
    Task<bool> IsValidWorktreeAsync(string worktreePath, string gitRoot);
    /// <summary>执行 git 命令</summary>
    Task<GitCommandResult> ExecuteGitCommandAsync(string workingDirectory, string arguments, CancellationToken cancellationToken = default);
    /// <summary>检查是否存在本地分支</summary>
    Task<bool> HasLocalBranchAsync(string gitRoot, string branchName, CancellationToken cancellationToken);
    /// <summary>应用稀疏检出</summary>
    Task<bool> ApplySparseCheckoutAsync(string worktreePath, IReadOnlyList<string> sparsePaths, CancellationToken cancellationToken);
}
