namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 配置复制中间件 — 复制配置文件 + .worktreeinclude + hooks 路径 + 符号链接（全部 best-effort）
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware))]
public sealed partial class WorktreeConfigMiddleware : IWorktreeCreateMiddleware
{
    [Inject] private readonly IFileOperationService _fs;
    [Inject] private readonly Lazy<IWorktreePipelineOperations> _worktreeService;
    [Inject] private readonly ILogger<WorktreeConfigMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(WorktreeCreateContext context, MiddlewareDelegate<WorktreeCreateContext> next, CancellationToken ct)
    {
        var opts = context.Options ?? new WorktreeOptions();
        var gitRoot = context.GitRoot;
        var worktreePath = context.WorktreePath;

        await CopyConfigFilesAsync(gitRoot, worktreePath, opts).ConfigureAwait(false);
        await CopyWorktreeIncludeFilesAsync(gitRoot, worktreePath, ct).ConfigureAwait(false);
        await ConfigureWorktreeHooksPathAsync(gitRoot, worktreePath, ct).ConfigureAwait(false);

        if (opts.SymlinkDirectories?.Count > 0)
        {
            await CreateSymlinksAsync(gitRoot, worktreePath, opts.SymlinkDirectories).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task CopyConfigFilesAsync(string gitRoot, string worktreePath, WorktreeOptions options)
    {
        if (options.ConfigFilesToCopy is not { Count: > 0 })
        {
            return;
        }

        foreach (var relativePath in options.ConfigFilesToCopy)
        {
            try
            {
                var sourcePath = _fs.CombinePath(gitRoot, relativePath);
                var destPath = _fs.CombinePath(worktreePath, relativePath);

                if (!_fs.FileExists(sourcePath))
                {
                    continue;
                }

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !_fs.DirectoryExists(destDir))
                {
                    _fs.CreateDirectory(destDir);
                }

                await _fs.CopyFileAsync(sourcePath, destPath, overwrite: true).ConfigureAwait(false);
                _logger?.LogDebug("复制配置文件: {Source} -> {Dest}", sourcePath, destPath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "复制配置文件失败: {File}", relativePath);
            }
        }
    }

    private async Task CopyWorktreeIncludeFilesAsync(string gitRoot, string worktreePath, CancellationToken ct)
    {
        var includeFilePath = _fs.CombinePath(gitRoot, ".worktreeinclude");
        if (!_fs.FileExists(includeFilePath))
        {
            return;
        }

        try
        {
            var readResult = await _fs.ReadFileAsync(includeFilePath, cancellationToken: ct).ConfigureAwait(false);
            if (!readResult.Success) return;

            var patterns = readResult.Content
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToList();

            if (patterns.Count == 0) return;

            var lsResult = await _worktreeService.Value.ExecuteGitCommandAsync(
                gitRoot,
                "ls-files --others --ignored --exclude-standard --directory",
                ct).ConfigureAwait(false);

            if (!lsResult.Success || string.IsNullOrWhiteSpace(lsResult.Output)) return;

            var entries = lsResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var entry in entries)
            {
                var trimmed = entry.Trim();
                if (trimmed.EndsWith('/')) continue;

                var matches = patterns.Any(p => MatchesWorktreeIncludePattern(trimmed, p.TrimStart('/')));
                if (!matches) continue;

                var srcPath = _fs.CombinePath(gitRoot, trimmed);
                var destPath = _fs.CombinePath(worktreePath, trimmed);

                if (!_fs.FileExists(srcPath)) continue;

                try
                {
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !_fs.DirectoryExists(destDir))
                    {
                        _fs.CreateDirectory(destDir);
                    }
                    await _fs.CopyFileAsync(srcPath, destPath, overwrite: true, cancellationToken: ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "复制 .worktreeinclude 文件失败: {File}", trimmed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "处理 .worktreeinclude 时出错");
        }
    }

    private async Task ConfigureWorktreeHooksPathAsync(string gitRoot, string worktreePath, CancellationToken ct)
    {
        try
        {
            var hooksResult = await _worktreeService.Value.ExecuteGitCommandAsync(
                gitRoot, "config --get core.hooksPath", ct).ConfigureAwait(false);

            if (hooksResult.Success && !string.IsNullOrWhiteSpace(hooksResult.Output))
            {
                var hooksPath = hooksResult.Output.Trim();
                var absHooksPath = Path.IsPathRooted(hooksPath)
                    ? hooksPath
                    : _fs.CombinePath(gitRoot, hooksPath);

                if (_fs.DirectoryExists(absHooksPath))
                {
                    await _worktreeService.Value.ExecuteGitCommandAsync(
                        worktreePath,
                        $"config core.hooksPath \"{absHooksPath}\"",
                        ct).ConfigureAwait(false);
                    _logger?.LogDebug("配置 worktree hooks 路径: {Path}", absHooksPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "配置 worktree hooks 路径失败");
        }
    }

    private async Task CreateSymlinksAsync(string gitRoot, string worktreePath, IReadOnlyList<string> directories)
    {
        foreach (var dir in directories)
        {
            try
            {
                var sourcePath = _fs.CombinePath(gitRoot, dir);
                var destPath = _fs.CombinePath(worktreePath, dir);

                if (!_fs.DirectoryExists(sourcePath))
                {
                    continue;
                }

                if (_fs.DirectoryExists(destPath) || _fs.FileExists(destPath))
                {
                    continue;
                }

                _fs.CreateSymbolicLink(destPath, sourcePath);
                _logger?.LogDebug("创建符号链接: {Source} -> {Dest}", sourcePath, destPath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "创建符号链接失败: {Directory}", dir);
            }
        }
    }

    private static bool MatchesWorktreeIncludePattern(string filePath, string pattern)
    {
        if (pattern.Contains('*'))
        {
            var regexStr = "^" + Regex.Escape(pattern).Replace("\\*\\*", ".*").Replace("\\*", "[^/]*") + "$";
            try
            {
                return Regex.IsMatch(filePath, regexStr, RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }
        return filePath.StartsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
               filePath.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
