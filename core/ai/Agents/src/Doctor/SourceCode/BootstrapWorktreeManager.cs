namespace Core.Agents.Doctor;

using JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 自举 worktree 管理器 — 为 Doctor 的自修改创建隔离的 git worktree
/// </summary>
public sealed class BootstrapWorktreeManager : IBootstrapWorktreeManager
{
    private readonly IFileSystem _fs;
    private BootstrapWorktree? _current;

    public BootstrapWorktreeManager(IFileSystem fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

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

    public Task<BootstrapWorktree?> GetCurrentAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_current);
    }

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

    private static async Task CleanupStaleBranchesAsync(string gitRoot, CancellationToken ct)
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

    private static async Task<string> ExecuteGitCommandAsync(string workingDir, string arguments, CancellationToken ct)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git 进程");

        var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        _ = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return output.Trim();
    }
}
