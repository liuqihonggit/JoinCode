namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 恢复中间件 — 检查现有 worktree 是否可恢复，命中时短路
/// 对齐 TS readWorktreeHeadSha：直接读取 .git 指针文件获取 HEAD SHA（无子进程，~15ms 优化）
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

        var existingHeadSha = ReadWorktreeHeadSha(worktreePath);
        if (existingHeadSha is not null)
        {
            _logger?.LogInformation("恢复现有 worktree: {WorktreePath}, Agent: {AgentId}", worktreePath, context.AgentId);

            try
            {
                _fs.SetDirectoryLastWriteTimeUtc(worktreePath, _clock.GetUtcNow());
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "更新 worktree mtime 失败: {Path}", worktreePath);
            }

            var existingSession = new AgentWorktreeSession
            {
                AgentId = context.AgentId,
                OriginalCwd = context.OriginalCwd,
                WorktreePath = worktreePath,
                BranchName = branchName,
                GitRootPath = context.GitRoot,
                BaseCommitSha = existingHeadSha,
                CreatedAt = _clock.GetUtcNow(),
                Existed = true
            };

            await _worktreeService.Value.SaveSessionAsync(existingSession).ConfigureAwait(false);

            context.IsRecovery = true;
            context.RecoveredSession = existingSession;
            context.WorktreePath = worktreePath;
            context.BranchName = branchName;
            context.BaseCommitSha = existingHeadSha;
            context.Result = WorktreeCreateResult.SuccessResult(existingSession, true);
            return;
        }

        context.WorktreePath = worktreePath;
        context.BranchName = branchName;

        await next(context, ct).ConfigureAwait(false);
    }

    private string? ReadWorktreeHeadSha(string worktreePath)
    {
        try
        {
            var gitFile = _fs.CombinePath(worktreePath, ".git");
            if (!_fs.FileExists(gitFile)) return null;

            var readResult = _fs.ReadFileAsync(gitFile).GetAwaiter().GetResult();
            if (!readResult.Success) return null;

            var content = readResult.Content.Trim();
            if (!content.StartsWith("gitdir: ", StringComparison.OrdinalIgnoreCase)) return null;

            var gitdirRelative = content["gitdir: ".Length..].Trim();
            var gitdirAbs = _fs.CombinePath(worktreePath, gitdirRelative);
            var normalizedGitdir = Path.GetFullPath(gitdirAbs);

            var headFile = _fs.CombinePath(normalizedGitdir, "HEAD");
            if (!_fs.FileExists(headFile)) return null;

            var headReadResult = _fs.ReadFileAsync(headFile).GetAwaiter().GetResult();
            if (!headReadResult.Success) return null;

            var headContent = headReadResult.Content.Trim();

            if (headContent.StartsWith("ref: ", StringComparison.OrdinalIgnoreCase))
            {
                var refPath = headContent["ref: ".Length..].Trim();
                var refFile = _fs.CombinePath(normalizedGitdir, refPath);
                if (!_fs.FileExists(refFile)) return null;

                var refReadResult = _fs.ReadFileAsync(refFile).GetAwaiter().GetResult();
                return refReadResult.Success ? refReadResult.Content.Trim() : null;
            }

            return headContent.Length == 40 && headContent.All(c => char.IsAsciiHexDigit(c)) ? headContent : null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "读取 worktree HEAD SHA 失败: {Path}", worktreePath);
            return null;
        }
    }
}
