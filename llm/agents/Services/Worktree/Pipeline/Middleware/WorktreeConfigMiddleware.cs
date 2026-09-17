namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 配置复制中间件 — 复制配置文件 + .worktreeinclude + hooks 路径 + 符号链接（全部 best-effort）
/// </summary>
[Register(typeof(IWorktreeCreateMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorktreeConfigMiddleware : ServiceEntity, IWorktreeCreateMiddleware
{

    /// <summary>
    /// 构造 WorktreeConfigMiddleware 实例，注入文件操作服务、延迟加载的管道操作及日志器
    /// </summary>
    public WorktreeConfigMiddleware(IFileOperationService fs, Lazy<IWorktreePipelineOperations> worktreeService, ILogger<WorktreeConfigMiddleware>? logger = null)
    {
        _fs = fs;
        _worktreeService = worktreeService;
        _logger = logger;
    }
    private readonly IFileOperationService _fs;
    private readonly Lazy<IWorktreePipelineOperations> _worktreeService;
    private readonly ILogger<WorktreeConfigMiddleware>? _logger;

    /// <summary>中间件错误处理策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行配置复制：复制配置文件、.worktreeinclude 文件、hooks 路径与符号链接（全部 best-effort）
    /// </summary>
    /// <param name="context">worktree 创建上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
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
            var collapsedDirs = new List<string>();
            var files = new List<string>();

            foreach (var entry in entries)
            {
                var trimmed = entry.Trim();
                if (trimmed.EndsWith('/'))
                {
                    collapsedDirs.Add(trimmed);
                    continue;
                }

                if (patterns.Any(p => WorktreeIncludePatternMatcher.Matches(trimmed, p.TrimStart('/'))))
                {
                    files.Add(trimmed);
                }
            }

            if (collapsedDirs.Count > 0)
            {
                var dirsToExpand = collapsedDirs.Where(dir =>
                {
                    var dirNoSlash = dir[..^1];
                    if (patterns.Any(p => PatternTargetsCollapsedDir(p, dir))) return true;
                    if (patterns.Any(p => WorktreeIncludePatternMatcher.Matches(dirNoSlash, p.TrimStart('/')))) return true;
                    return false;
                }).ToList();

                if (dirsToExpand.Count > 0)
                {
                    var expandArgs = "ls-files --others --ignored --exclude-standard -- " +
                                     string.Join(" ", dirsToExpand.Select(d => $"\"{d}\""));
                    var expandedResult = await _worktreeService.Value.ExecuteGitCommandAsync(
                        gitRoot, expandArgs, ct).ConfigureAwait(false);

                    if (expandedResult.Success && !string.IsNullOrWhiteSpace(expandedResult.Output))
                    {
                        foreach (var f in expandedResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var trimmed = f.Trim();
                            if (patterns.Any(p => WorktreeIncludePatternMatcher.Matches(trimmed, p.TrimStart('/'))))
                            {
                                files.Add(trimmed);
                            }
                        }
                    }
                }
            }

            foreach (var relativePath in files)
            {
                var srcPath = _fs.CombinePath(gitRoot, relativePath);
                var destPath = _fs.CombinePath(worktreePath, relativePath);

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
                    _logger?.LogWarning(ex, "复制 .worktreeinclude 文件失败: {File}", relativePath);
                }
            }

            if (files.Count > 0)
            {
                _logger?.LogInformation("从 .worktreeinclude 复制了 {Count} 个文件: {Files}", files.Count, string.Join(", ", files));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "处理 .worktreeinclude 时出错");
        }
    }

    private static bool PatternTargetsCollapsedDir(string pattern, string collapsedDir)
    {
        var normalized = pattern.StartsWith('/') ? pattern[1..] : pattern;
        if (normalized.StartsWith(collapsedDir)) return true;
        var globIdx = normalized.IndexOfAny(['*', '?', '[']);
        if (globIdx > 0)
        {
            var literalPrefix = normalized[..globIdx];
            if (collapsedDir.StartsWith(literalPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private async Task ConfigureWorktreeHooksPathAsync(string gitRoot, string worktreePath, CancellationToken ct)
    {
        try
        {
            string? hooksPath = null;

            var huskyPath = _fs.CombinePath(gitRoot, ".husky");
            if (_fs.DirectoryExists(huskyPath))
            {
                hooksPath = huskyPath;
            }
            else
            {
                var gitHooksPath = _fs.CombinePath(gitRoot, ".git", "hooks");
                if (_fs.DirectoryExists(gitHooksPath))
                {
                    hooksPath = gitHooksPath;
                }
            }

            if (hooksPath is null) return;

            var existingHooksResult = await _worktreeService.Value.ExecuteGitCommandAsync(
                gitRoot, "config --get core.hooksPath", ct).ConfigureAwait(false);

            var existingHooksPath = existingHooksResult.Success ? existingHooksResult.Output.Trim() : null;
            if (string.Equals(existingHooksPath, hooksPath, StringComparison.OrdinalIgnoreCase)) return;

            await _worktreeService.Value.ExecuteGitCommandAsync(
                worktreePath,
                $"config core.hooksPath \"{hooksPath}\"",
                ct).ConfigureAwait(false);
            _logger?.LogDebug("配置 worktree hooks 路径: {Path}", hooksPath);
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

}
