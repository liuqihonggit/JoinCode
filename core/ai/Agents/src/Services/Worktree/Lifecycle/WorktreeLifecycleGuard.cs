namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 生命周期守卫 — 构造时锁定 worktree 路径，Dispose 时用同一路径删除，从不二次计算。
/// <para>设计原理：路径在构造时传入并锁定为 readonly 字段，整个生命周期都用这同一个 path。</para>
/// <para>这从根上消除了"创建用当前目录、清理用主仓库"的路径不一致 bug —— 因为根本没有第二次计算路径的机会。</para>
/// <para>异步释放优先（IAsyncDisposable.DisposeAsync），同步 Dispose 委托异步完成。</para>
/// <para>无终结器：worktree 删除需 git 命令（托管对象），终结器中不安全，依赖显式 DisposeAsync/Dispose。</para>
/// </summary>
public sealed class WorktreeLifecycleGuard : IAsyncDisposable
{
    private readonly string _worktreePath;
    private readonly string _mainPath;
    private readonly IFileOperationService _fileSystem;
    private readonly IGitCommandRunner? _gitRunner;
    private readonly string? _branchName;
    private readonly ILogger? _logger;
    private int _disposed;

    /// <summary>
    /// 构造时锁定 worktree 路径 — 路径在此刻传入，之后永不重新计算。
    /// </summary>
    /// <param name="worktreePath">要生成的 worktree 路径（构造时锁定）</param>
    /// <param name="mainPath">主仓库路径（用于安全校验 + git 命令 cwd）</param>
    /// <param name="fileSystem">文件系统抽象</param>
    /// <param name="gitRunner">git 命令执行器（null 时 Dispose 仅做路径校验不执行 git）</param>
    /// <param name="branchName">worktree 分支名（可选，Dispose 时一并删除）</param>
    /// <param name="logger">日志</param>
    /// <exception cref="ArgumentException">路径为 null/空白，或 worktree 路径等于主仓库路径</exception>
    public WorktreeLifecycleGuard(
        string worktreePath,
        string mainPath,
        IFileOperationService fileSystem,
        IGitCommandRunner? gitRunner = null,
        string? branchName = null,
        ILogger? logger = null)
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

        _worktreePath = normalizedWorktree;
        _mainPath = normalizedMain;
        _fileSystem = fileSystem;
        _gitRunner = gitRunner;
        _branchName = branchName;
        _logger = logger;
    }

    /// <summary>
    /// 锁定的 worktree 路径 — 构造后永不改变。
    /// </summary>
    public string WorktreePath => _worktreePath;

    /// <summary>
    /// 主仓库路径 — 构造后永不改变。
    /// </summary>
    public string MainPath => _mainPath;

    /// <summary>
    /// 释放 worktree — 用构造时锁定的路径执行 git worktree remove，从不重新计算路径。
    /// </summary>
    /// <param name="force">是否强制删除（有未提交变更时需 true）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<WorktreeGuardResult> ReleaseAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return await ReleaseCoreAsync(force, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步释放 — 用构造时锁定的路径执行 git worktree remove。消费方用 await using。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try
        {
            await ReleaseCoreAsync(force: true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "DisposeAsync 清理失败: {Path}", _worktreePath);
        }
    }

    private async Task<WorktreeGuardResult> ReleaseCoreAsync(bool force, CancellationToken cancellationToken)
    {
        if (_gitRunner is null)
        {
            return WorktreeGuardResult.Fail("gitRunner 未注入，无法执行删除");
        }

        var exists = await _fileSystem.DirectoryExistsAsync(_worktreePath, cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            return WorktreeGuardResult.Fail($"worktree 路径不存在: {_worktreePath}");
        }

        var forceArg = force ? " --force" : string.Empty;
        var removeResult = await _gitRunner.ExecuteAsync(
            $"worktree remove{forceArg} \"{_worktreePath}\"",
            _mainPath,
            cancellationToken).ConfigureAwait(false);

        if (!removeResult.Success)
        {
            return WorktreeGuardResult.Fail($"移除 worktree 失败: {removeResult.Error}");
        }

        if (_branchName is not null)
        {
            await _gitRunner.ExecuteAsync(
                $"branch -D {_branchName}",
                _mainPath,
                cancellationToken).ConfigureAwait(false);
        }

        _logger?.LogInformation("WorktreeLifecycleGuard 释放: {Path}", _worktreePath);
        return WorktreeGuardResult.Ok(force);
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
/// Worktree 守卫操作结果。
/// </summary>
public sealed record WorktreeGuardResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Forced { get; init; }

    public static WorktreeGuardResult Ok(bool forced = false) => new() { Success = true, Forced = forced };
    public static WorktreeGuardResult Fail(string error) => new() { Success = false, ErrorMessage = error };
}
