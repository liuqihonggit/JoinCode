namespace Core.Agents.Worktree;

/// <summary>
/// Worktree Git 根查找中间件 — 查找 Git 仓库根目录
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware))]
public sealed partial class WorktreeGitRootMiddleware : IWorktreeCreateMiddleware
{
    [Inject] private readonly IFileOperationService _fs;
    [Inject] private readonly ILogger<WorktreeGitRootMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        var gitRoot = context.GitRootPath ?? await FindGitRootAsync(_fs.GetCurrentDirectory()).ConfigureAwait(false);
        if (string.IsNullOrEmpty(gitRoot))
        {
            context.Fail("未找到 Git 仓库根目录");
            return;
        }

        context.GitRoot = gitRoot;
        context.OriginalCwd = _fs.GetCurrentDirectory();

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task<string?> FindGitRootAsync(string startPath)
    {
        var currentPath = startPath;

        while (!string.IsNullOrEmpty(currentPath))
        {
            var gitDir = _fs.CombinePath(currentPath, ".git");
            if (_fs.DirectoryExists(gitDir))
            {
                return currentPath;
            }

            if (_fs.FileExists(gitDir))
            {
                var canonicalRoot = ResolveCanonicalGitRootFromGitdirFile(gitDir, currentPath);
                return canonicalRoot ?? currentPath;
            }

            var parentPath = Path.GetDirectoryName(currentPath);
            if (parentPath == currentPath || string.IsNullOrEmpty(parentPath))
            {
                break;
            }
            currentPath = parentPath;
        }

        return null;
    }

    private string? ResolveCanonicalGitRootFromGitdirFile(string gitdirFilePath, string worktreePath)
    {
        try
        {
            var readResult = _fs.ReadFileAsync(gitdirFilePath).GetAwaiter().GetResult();
            if (!readResult.Success) return null;

            var content = readResult.Content.Trim();
            if (!content.StartsWith("gitdir: ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var gitdirRelative = content["gitdir: ".Length..].Trim();
            var gitdirAbs = _fs.CombinePath(worktreePath, gitdirRelative);
            var normalizedGitdir = Path.GetFullPath(gitdirAbs);

            var worktreesMarker = Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar + "worktrees" + Path.DirectorySeparatorChar;
            var markerIdx = normalizedGitdir.IndexOf(worktreesMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIdx < 0)
            {
                return null;
            }

            var canonicalRoot = normalizedGitdir[..markerIdx];
            var canonicalGitDir = _fs.CombinePath(canonicalRoot, ".git");
            if (_fs.DirectoryExists(canonicalGitDir))
            {
                return canonicalRoot;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "解析 .gitdir 文件失败: {Path}", gitdirFilePath);
            return null;
        }
    }
}
