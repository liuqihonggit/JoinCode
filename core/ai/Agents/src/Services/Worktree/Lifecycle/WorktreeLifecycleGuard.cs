namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 生命周期守卫 — 专注路径验证与释放/删除安全防护。
/// <para>职责单一：仅负责 worktree 路径与主仓库路径的安全校验，防止误删主仓库。</para>
/// <para>bug 背景：worktree 创建在当前目录（如 D:\project\w1），但清理时 FindGitRootAsync 解析到主仓库
/// （D:\project\JoinCode），导致 git worktree remove 命令路径不匹配，清理失败，worktree 静默残留。</para>
/// <para>修复策略：所有释放/删除操作前必须经过 EnsureNotMainPath 校验，拒绝 worktree 路径等于主仓库路径。</para>
/// </summary>
public sealed class WorktreeLifecycleGuard
{
    private readonly IFileOperationService _fileSystem;

    /// <summary>
    /// 注入文件系统 — 生产用 PhysicalFileSystem，测试用 InMemoryFileOperationService。
    /// </summary>
    public WorktreeLifecycleGuard(IFileOperationService fileSystem) => _fileSystem = fileSystem;

    /// <summary>
    /// 验证 worktree 路径不等于主仓库路径 — 防止误删主仓库代码。
    /// <para>路径规范化后按 OrdinalIgnoreCase 比较，容忍大小写差异和尾部分隔符差异。</para>
    /// </summary>
    /// <param name="worktreePath">worktree 路径</param>
    /// <param name="mainPath">主仓库路径</param>
    /// <exception cref="ArgumentException">路径为 null/空白，或 worktree 路径等于主仓库路径</exception>
    public void EnsureNotMainPath(string worktreePath, string mainPath)
    {
        ThrowIfBlank(worktreePath, nameof(worktreePath));
        ThrowIfBlank(mainPath, nameof(mainPath));

        var normalizedWorktree = NormalizePath(worktreePath);
        var normalizedMain = NormalizePath(mainPath);

        if (string.Equals(normalizedWorktree, normalizedMain, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "worktree 路径不能等于主仓库路径，拒绝操作以防误删主仓库",
                nameof(worktreePath));
        }
    }

    /// <summary>
    /// 强制删除 worktree — 删除前校验路径安全，路径不存在时宽容返回失败。
    /// </summary>
    /// <param name="worktreePath">worktree 路径</param>
    /// <param name="mainPath">主仓库路径（用于安全校验）</param>
    /// <param name="force">是否强制删除</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除结果</returns>
    public async Task<WorktreeGuardResult> RemoveAsync(
        string worktreePath,
        string mainPath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        EnsureNotMainPath(worktreePath, mainPath);

        var exists = await _fileSystem.DirectoryExistsAsync(worktreePath, cancellationToken).ConfigureAwait(false);

        if (!exists)
        {
            return WorktreeGuardResult.Fail($"worktree 路径不存在: {worktreePath}");
        }

        return WorktreeGuardResult.Ok(force);
    }

    /// <summary>
    /// 释放 worktree — 无变更时删除，有变更时保留。删除前校验路径安全。
    /// </summary>
    /// <param name="worktreePath">worktree 路径</param>
    /// <param name="mainPath">主仓库路径（用于安全校验）</param>
    /// <param name="hasChanges">是否有未提交变更</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>释放结果（Kept=true 表示因有变更而保留）</returns>
    public Task<WorktreeGuardResult> ReleaseAsync(
        string worktreePath,
        string mainPath,
        bool hasChanges,
        CancellationToken cancellationToken = default)
    {
        EnsureNotMainPath(worktreePath, mainPath);

        if (hasChanges)
        {
            return Task.FromResult(WorktreeGuardResult.Keep("has_changes"));
        }

        return RemoveAsync(worktreePath, mainPath, force: false, cancellationToken);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return path.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static void ThrowIfBlank(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("路径不能为 null 或空白", paramName);
        }
    }
}

/// <summary>
/// Worktree 守卫操作结果 — 专注路径验证与删除/释放结果。
/// </summary>
public sealed record WorktreeGuardResult
{
    public required bool Success { get; init; }
    public bool Kept { get; init; }
    public string? Reason { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Forced { get; init; }

    public static WorktreeGuardResult Ok(bool forced = false) => new() { Success = true, Forced = forced };
    public static WorktreeGuardResult Fail(string error) => new() { Success = false, ErrorMessage = error };
    public static WorktreeGuardResult Keep(string reason) => new() { Success = true, Kept = true, Reason = reason };
}
