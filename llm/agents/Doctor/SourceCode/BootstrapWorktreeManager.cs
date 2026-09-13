namespace Core.Agents.Doctor;


/// <summary>
/// 自举 worktree 管理器 — 为 Doctor 的自修改创建隔离的 git worktree
/// </summary>
public sealed class BootstrapWorktreeManager : IBootstrapWorktreeManager
{
    private readonly IFileSystem _fs;
    private readonly IGitCommandRunner _gitRunner;
    private BootstrapWorktree? _current;

    /// <summary>
    /// 构造自举 worktree 管理器
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="gitRunner">Git 命令执行器</param>
    public BootstrapWorktreeManager(IFileSystem fs, IGitCommandRunner gitRunner)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
    }

    /// <summary>
    /// 创建隔离 worktree — 在 .jcc/worktrees/doctor-bootstrap 下创建新分支的工作树
    /// </summary>
    /// <param name="gitRoot">Git 仓库根目录</param>
    /// <param name="baseRef">基线引用（可选，默认 HEAD）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>创建的 worktree 信息</returns>
    public async Task<BootstrapWorktree> CreateAsync(
        string gitRoot,
        string? baseRef = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gitRoot);
        ct.ThrowIfCancellationRequested();

        await CleanupStaleBranchesAsync(gitRoot, ct).ConfigureAwait(false);

        var effectiveBaseRef = baseRef ?? "HEAD";
        var branchName = $"doctor-bootstrap-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var worktreePath = Path.Combine(gitRoot, ".jcc", "worktrees", "doctor-bootstrap");

        DoctorDiag.Write($"[Doctor] 创建自举 worktree: {worktreePath}, 分支: {branchName}, 基于: {effectiveBaseRef}");

        try
        {
            if (_fs.DirectoryExists(worktreePath))
            {
                DoctorDiag.Write($"[Doctor] worktree 目录已存在，先清理: {worktreePath}");
                await ExecuteGitCommandAsync(gitRoot, $"worktree remove --force \"{worktreePath}\"", ct).ConfigureAwait(false);
            }

            await ExecuteGitCommandAsync(gitRoot, $"worktree add -B {branchName} \"{worktreePath}\" {effectiveBaseRef}", ct).ConfigureAwait(false);

            DoctorDiag.Write($"[Doctor] 自举 worktree 创建成功: {worktreePath}");
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Doctor] 创建 worktree 失败: {ex.Message}，使用目录降级模式");
        }

        _fs.CreateDirectory(worktreePath);

        _current = new BootstrapWorktree
        {
            WorktreePath = worktreePath,
            BranchName = branchName,
            BaseRef = effectiveBaseRef,
            GitRoot = gitRoot
        };

        return _current;
    }

    /// <summary>
    /// 获取当前活跃的 worktree
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>当前 worktree（无则 null）</returns>
    public Task<BootstrapWorktree?> GetCurrentAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_current);
    }

    /// <summary>
    /// 提交 worktree 中的修改 — git add -A + commit，返回变更文件列表和 diff
    /// </summary>
    /// <param name="message">提交消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>提交结果</returns>
    public async Task<WorktreeCommitResult> CommitChangesAsync(
        string message,
        CancellationToken ct = default)
    {
        if (_current is null)
        {
            return new WorktreeCommitResult
            {
                Success = false,
                FailureReason = "没有活跃的 worktree"
            };
        }

        try
        {
            await ExecuteGitCommandAsync(_current.WorktreePath, "add -A", ct).ConfigureAwait(false);

            var statusResult = await ExecuteGitCommandAsync(_current.WorktreePath, "status --porcelain", ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(statusResult))
            {
                return new WorktreeCommitResult
                {
                    Success = true,
                    ChangedFiles = []
                };
            }

            var commitResult = await ExecuteGitCommandAsync(_current.WorktreePath, $"commit -m \"{message}\"", ct).ConfigureAwait(false);

            var diffResult = await ExecuteGitCommandAsync(_current.WorktreePath, "diff HEAD~1", ct).ConfigureAwait(false);

            var changedFiles = statusResult.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Length > 3 ? line[3..].Trim() : line.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            return new WorktreeCommitResult
            {
                Success = true,
                Diff = diffResult,
                ChangedFiles = changedFiles
            };
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Doctor] 提交 worktree 修改失败: {ex.Message}");
            return new WorktreeCommitResult
            {
                Success = false,
                FailureReason = ex.Message
            };
        }
    }

    /// <summary>
    /// 清理 worktree — 移除工作树并删除分支（非致命，失败仅记录日志）
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task CleanupAsync(CancellationToken ct = default)
    {
        if (_current is null) return;

        DoctorDiag.Write($"[Doctor] 清理自举 worktree: {_current.WorktreePath}");

        try
        {
            await ExecuteGitCommandAsync(_current.GitRoot, $"worktree remove --force \"{_current.WorktreePath}\"", ct).ConfigureAwait(false);
            await ExecuteGitCommandAsync(_current.GitRoot, $"branch -D {_current.BranchName}", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Doctor] 清理 worktree 失败（非致命）: {ex.Message}");
        }

        _current = null;
    }

    private async Task CleanupStaleBranchesAsync(string gitRoot, CancellationToken ct)
    {
        try
        {
            var branchList = await ExecuteGitCommandAsync(gitRoot, "branch --list doctor-bootstrap-*", ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(branchList))
            {
                return;
            }

            var branches = branchList.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim().TrimStart('*').Trim())
                .Where(b => b.StartsWith("doctor-bootstrap-", StringComparison.Ordinal))
                .ToList();

            if (branches.Count == 0)
            {
                return;
            }

            DoctorDiag.Write($"[Doctor] 清理 {branches.Count} 个残留的 doctor-bootstrap 分支");

            foreach (var branch in branches)
            {
                try
                {
                    await ExecuteGitCommandAsync(gitRoot, $"branch -D {branch}", ct).ConfigureAwait(false);
                    DoctorDiag.Write($"[Doctor] 已删除残留分支: {branch}");
                }
                catch (Exception ex)
                {
                    DoctorDiag.WriteError($"[Doctor] 删除残留分支失败: {branch}, {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DoctorDiag.WriteError($"[Doctor] 清理残留分支时出错（非致命）: {ex.Message}");
        }
    }

    private async Task<string> ExecuteGitCommandAsync(string workingDir, string arguments, CancellationToken ct)
    {
        var result = await _gitRunner.ExecuteAsync(arguments, workingDir, ct).ConfigureAwait(false);
        return result.Output.Trim();
    }
}
