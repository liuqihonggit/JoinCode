namespace Core.Agents;

[Register(typeof(IWorktreeMergeService))]
public sealed partial class WorktreeMergeService : ServiceEntity, IWorktreeMergeService
{

    public WorktreeMergeService(IGitCommandRunner gitRunner, IFileSystem fileSystem, ILogger<WorktreeMergeService>? logger = null)
    {
        _gitRunner = gitRunner;
        _fileSystem = fileSystem;
        _logger = logger;
    }
    private readonly ILogger<WorktreeMergeService>? _logger;
    private readonly IGitCommandRunner _gitRunner;
    private readonly IFileSystem _fileSystem;

    public async Task<WorktreeMergeResult> MergeToTargetAsync(
        string sourceWorktreePath,
        string targetWorktreePath,
        WorktreeMergeStrategy strategy = WorktreeMergeStrategy.Fail,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetWorktreePath);

        var sourceFiles = await GetChangedFilesAsync(sourceWorktreePath, cancellationToken).ConfigureAwait(false);
        var targetFiles = await GetChangedFilesAsync(targetWorktreePath, cancellationToken).ConfigureAwait(false);

        var conflictingFiles = sourceFiles.Intersect(targetFiles, StringComparer.OrdinalIgnoreCase).ToList();

        if (conflictingFiles.Count == 0)
        {
            _logger?.LogInformation("[WorktreeMerge] 无文件冲突，使用 git apply patch 合并 {Source} → {Target}",
                sourceWorktreePath, targetWorktreePath);
            return await ApplyPatchAsync(sourceWorktreePath, targetWorktreePath, cancellationToken).ConfigureAwait(false);
        }

        _logger?.LogInformation("[WorktreeMerge] 检测到 {Count} 个冲突文件，使用 git merge 分支合并: {Files}",
            conflictingFiles.Count, string.Join(", ", conflictingFiles));

        return await MergeBranchAsync(sourceWorktreePath, targetWorktreePath, strategy, conflictingFiles, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorktreeMergeResult> ApplyPatchAsync(
        string sourceWorktreePath,
        string targetWorktreePath,
        CancellationToken cancellationToken)
    {
        var diffResult = await ExecuteGitAsync(sourceWorktreePath, "diff HEAD", cancellationToken).ConfigureAwait(false);
        if (!diffResult.Success)
        {
            return WorktreeMergeResult.Failed(sourceWorktreePath, targetWorktreePath, $"git diff failed: {diffResult.Error}");
        }

        if (string.IsNullOrWhiteSpace(diffResult.Output))
        {
            _logger?.LogInformation("[WorktreeMerge] Source worktree 无改动，跳过合并");
            return WorktreeMergeResult.Success(sourceWorktreePath, targetWorktreePath, [], "patch-skip");
        }

        var patchPath = Path.Combine(Path.GetTempPath(), $"merge-patch-{Guid.NewGuid():N}.diff");
        try
        {
            await _fileSystem.WriteAllTextAsync(patchPath, diffResult.Output, cancellationToken).ConfigureAwait(false);

            var applyResult = await ExecuteGitAsync(targetWorktreePath, $"apply \"{patchPath}\"", cancellationToken).ConfigureAwait(false);
            if (!applyResult.Success)
            {
                var checkResult = await ExecuteGitAsync(targetWorktreePath, $"apply --check \"{patchPath}\"", cancellationToken).ConfigureAwait(false);
                return WorktreeMergeResult.Failed(sourceWorktreePath, targetWorktreePath,
                    $"git apply failed: {applyResult.Error}", [checkResult.Error]);
            }

            var changedFiles = ParseDiffFiles(diffResult.Output);
            return WorktreeMergeResult.Success(sourceWorktreePath, targetWorktreePath, changedFiles, "patch");
        }
        finally
        {
            if (_fileSystem.FileExists(patchPath))
            {
                _fileSystem.DeleteFile(patchPath);
            }
        }
    }

    private async Task<WorktreeMergeResult> MergeBranchAsync(
        string sourceWorktreePath,
        string targetWorktreePath,
        WorktreeMergeStrategy strategy,
        IReadOnlyList<string> conflictFiles,
        CancellationToken cancellationToken)
    {
        var branchResult = await ExecuteGitAsync(sourceWorktreePath, "branch --show-current", cancellationToken).ConfigureAwait(false);
        var sourceBranch = branchResult.Success && !string.IsNullOrWhiteSpace(branchResult.Output)
            ? branchResult.Output.Trim()
            : $"worktree-merge-{Guid.NewGuid():N}";

        if (!branchResult.Success || string.IsNullOrWhiteSpace(branchResult.Output))
        {
            var checkoutResult = await ExecuteGitAsync(sourceWorktreePath, $"checkout -b {sourceBranch}", cancellationToken).ConfigureAwait(false);
            if (!checkoutResult.Success)
            {
                return WorktreeMergeResult.Failed(sourceWorktreePath, targetWorktreePath, $"Failed to create branch: {checkoutResult.Error}");
            }
        }

        var addResult = await ExecuteGitAsync(sourceWorktreePath, "add -A", cancellationToken).ConfigureAwait(false);
        if (!addResult.Success)
        {
            return WorktreeMergeResult.Failed(sourceWorktreePath, targetWorktreePath, $"git add failed: {addResult.Error}");
        }

        var commitResult = await ExecuteGitAsync(sourceWorktreePath, "commit -m \"worktree-merge: auto-commit before merge\"", cancellationToken).ConfigureAwait(false);
        if (!commitResult.Success && !commitResult.Error.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
        {
            return WorktreeMergeResult.Failed(sourceWorktreePath, targetWorktreePath, $"git commit failed: {commitResult.Error}");
        }

        var preCheckConflicts = await PreCheckMergeConflictsAsync(sourceWorktreePath, targetWorktreePath, sourceBranch, strategy, cancellationToken).ConfigureAwait(false);
        if (preCheckConflicts is not null)
        {
            return preCheckConflicts;
        }

        var mergeResult = await ExecuteGitAsync(targetWorktreePath, $"merge {sourceBranch} --no-edit", cancellationToken).ConfigureAwait(false);

        if (mergeResult.Success)
        {
            var changedFiles = await GetChangedFilesAsync(targetWorktreePath, cancellationToken).ConfigureAwait(false);
            return WorktreeMergeResult.Success(sourceWorktreePath, targetWorktreePath, changedFiles, "merge");
        }

        if (mergeResult.Error.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleConflictAsync(sourceWorktreePath, targetWorktreePath, strategy, cancellationToken).ConfigureAwait(false);
        }

        await ExecuteGitAsync(targetWorktreePath, "merge --abort", cancellationToken).ConfigureAwait(false);
        return WorktreeMergeResult.Failed(sourceWorktreePath, targetWorktreePath, $"git merge failed: {mergeResult.Error}", conflictFiles);
    }

    private async Task<WorktreeMergeResult> HandleConflictAsync(
        string sourceWorktreePath,
        string targetWorktreePath,
        WorktreeMergeStrategy strategy,
        CancellationToken cancellationToken)
    {
        var conflictListResult = await ExecuteGitAsync(targetWorktreePath, "diff --name-only --diff-filter=U", cancellationToken).ConfigureAwait(false);
        var conflicts = conflictListResult.Success
            ? conflictListResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList()
            : [];

        switch (strategy)
        {
            case WorktreeMergeStrategy.Ours:
                foreach (var file in conflicts)
                {
                    await ExecuteGitAsync(targetWorktreePath, $"checkout --ours \"{file}\"", cancellationToken).ConfigureAwait(false);
                    await ExecuteGitAsync(targetWorktreePath, $"add \"{file}\"", cancellationToken).ConfigureAwait(false);
                }
                break;

            case WorktreeMergeStrategy.Theirs:
                foreach (var file in conflicts)
                {
                    await ExecuteGitAsync(targetWorktreePath, $"checkout --theirs \"{file}\"", cancellationToken).ConfigureAwait(false);
                    await ExecuteGitAsync(targetWorktreePath, $"add \"{file}\"", cancellationToken).ConfigureAwait(false);
                }
                break;

            case WorktreeMergeStrategy.AutoMerge:
                break;

            default:
                await ExecuteGitAsync(targetWorktreePath, "merge --abort", cancellationToken).ConfigureAwait(false);
                return WorktreeMergeResult.Failed(sourceWorktreePath, targetWorktreePath, "Merge conflict detected, strategy=Fail", conflicts);
        }

        var commitResult = await ExecuteGitAsync(targetWorktreePath, "commit --no-edit", cancellationToken).ConfigureAwait(false);
        if (!commitResult.Success)
        {
            await ExecuteGitAsync(targetWorktreePath, "merge --abort", cancellationToken).ConfigureAwait(false);
            return WorktreeMergeResult.Failed(sourceWorktreePath, targetWorktreePath, $"Conflict resolution commit failed: {commitResult.Error}", conflicts);
        }

        return new WorktreeMergeResult
        {
            SourceWorktreePath = sourceWorktreePath,
            TargetWorktreePath = targetWorktreePath,
            IsSuccess = true,
            HadConflicts = true,
            ConflictFiles = conflicts,
            StrategyUsed = $"merge-{strategy.ToString().ToLowerInvariant()}"
        };
    }

    private async Task<List<string>> GetChangedFilesAsync(string worktreePath, CancellationToken cancellationToken)
    {
        var result = await ExecuteGitAsync(worktreePath, "diff --name-only HEAD", cancellationToken).ConfigureAwait(false);
        if (!result.Success) return [];
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static List<string> ParseDiffFiles(string diffOutput)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in diffOutput.Split('\n'))
        {
            if (line.StartsWith("+++ b/", StringComparison.Ordinal) || line.StartsWith("--- a/", StringComparison.Ordinal))
            {
                var filePath = line[6..];
                files.Add(filePath);
            }
        }
        return files.ToList();
    }

    private async Task<WorktreeMergeResult?> PreCheckMergeConflictsAsync(
        string sourceWorktreePath,
        string targetWorktreePath,
        string sourceBranch,
        WorktreeMergeStrategy strategy,
        CancellationToken cancellationToken)
    {
        var staleCheck = await CheckStaleConflictMarkersAsync(sourceWorktreePath, targetWorktreePath, cancellationToken).ConfigureAwait(false);
        if (staleCheck is not null)
        {
            return staleCheck;
        }

        var targetBranchResult = await ExecuteGitAsync(targetWorktreePath, "branch --show-current", cancellationToken).ConfigureAwait(false);
        if (!targetBranchResult.Success || string.IsNullOrWhiteSpace(targetBranchResult.Output))
        {
            return null;
        }

        var targetBranch = targetBranchResult.Output.Trim();
        var conflictCheck = await _gitRunner.DetectMergeConflictAsync(targetBranch, sourceBranch, targetWorktreePath, cancellationToken).ConfigureAwait(false);

        if (!conflictCheck.HasConflict)
        {
            return null;
        }

        _logger?.LogInformation("[WorktreeMerge] 只读预检发现 {Count} 个冲突文件: {Files}",
            conflictCheck.ConflictFiles.Count, string.Join(", ", conflictCheck.ConflictFiles));

        if (strategy == WorktreeMergeStrategy.Fail)
        {
            return WorktreeMergeResult.Failed(
                sourceWorktreePath,
                targetWorktreePath,
                "Merge conflict detected (pre-merge read-only check), strategy=Fail",
                conflictCheck.ConflictFiles);
        }

        return null;
    }

    private async Task<WorktreeMergeResult?> CheckStaleConflictMarkersAsync(
        string sourceWorktreePath,
        string targetWorktreePath,
        CancellationToken cancellationToken)
    {
        var sourceCheck = await _gitRunner.DetectStaleConflictMarkersAsync(sourceWorktreePath, cancellationToken).ConfigureAwait(false);
        if (sourceCheck.HasStaleMarkers)
        {
            _logger?.LogError("[WorktreeMerge] 源 worktree 存在遗留冲突标记，禁止合并: {Files}",
                string.Join(", ", sourceCheck.Files));
            return WorktreeMergeResult.Failed(
                sourceWorktreePath,
                targetWorktreePath,
                "Stale conflict markers found in source worktree (unclean merge from previous run)",
                sourceCheck.Files);
        }

        var targetCheck = await _gitRunner.DetectStaleConflictMarkersAsync(targetWorktreePath, cancellationToken).ConfigureAwait(false);
        if (targetCheck.HasStaleMarkers)
        {
            _logger?.LogError("[WorktreeMerge] 目标 worktree 存在遗留冲突标记，禁止合并: {Files}",
                string.Join(", ", targetCheck.Files));
            return WorktreeMergeResult.Failed(
                sourceWorktreePath,
                targetWorktreePath,
                "Stale conflict markers found in target worktree (unclean merge from previous run)",
                targetCheck.Files);
        }

        return null;
    }

    private Task<GitCommandResult> ExecuteGitAsync(string workingDirectory, string arguments, CancellationToken cancellationToken)
        => _gitRunner.ExecuteAsync(arguments, workingDirectory, cancellationToken);
}
