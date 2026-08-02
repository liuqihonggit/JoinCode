namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 自举 worktree 管理器 — 为 Doctor 的自修改创建隔离的 git worktree
/// </summary>
public interface IBootstrapWorktreeManager
{
    /// <summary>
    /// 创建 Doctor 专属 worktree
    /// </summary>
    Task<BootstrapWorktree> CreateAsync(
        string gitRoot,
        string? baseRef = null,
        CancellationToken ct = default);

    /// <summary>
    /// 获取当前活跃的 worktree
    /// </summary>
    Task<BootstrapWorktree?> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>
    /// 提交 worktree 中的修改
    /// </summary>
    Task<WorktreeCommitResult> CommitChangesAsync(
        string message,
        CancellationToken ct = default);

    /// <summary>
    /// 清理 worktree（编译失败/放弃修复时）
    /// </summary>
    Task CleanupAsync(CancellationToken ct = default);
}
