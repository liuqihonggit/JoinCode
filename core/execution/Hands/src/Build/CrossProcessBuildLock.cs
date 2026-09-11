namespace Services.Build;

internal sealed class CrossProcessBuildLock : IAsyncDisposable
{
    private const string DefaultLockFileName = "JoinCode.Build.lock";
    private const string GitDirName = ".git";
    private static readonly string GitWorktreesMarker = $"{GitDirName}{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly string _lockPath;
    private Stream? _lockFile;
    private int _disposed;

    internal string LockPath => _lockPath;

    internal CrossProcessBuildLock(IFileSystem fs, ILogger? logger, string? lockPath = null)
    {
        _fs = fs;
        _logger = logger;
        _lockPath = lockPath ?? ResolveDefaultLockPath(fs);
    }

    internal async Task AcquireAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                _lockFile = _fs.CreateStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return;
            }
            catch (IOException ex)
            {
                _logger?.LogDebug(ex, "Build lock file is held by another process, retrying: {LockPath}", _lockPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.LogDebug(ex, "Build lock file access denied, retrying: {LockPath}", _lockPath);
            }

            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    internal void Release()
    {
        _lockFile?.Dispose();
        _lockFile = null;
    }

    private static string ResolveDefaultLockPath(IFileSystem fs)
    {
        var currentDir = fs.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(currentDir))
        {
            var gitPath = fs.CombinePath(currentDir, GitDirName);

            if (fs.DirectoryExists(gitPath))
                return fs.CombinePath(gitPath, DefaultLockFileName);

            if (fs.FileExists(gitPath))
            {
                var commonGitDir = ResolveCommonGitDir(fs, gitPath, currentDir);
                if (commonGitDir is not null && fs.DirectoryExists(commonGitDir))
                    return fs.CombinePath(commonGitDir, DefaultLockFileName);
            }

            var parent = fs.GetParentPath(currentDir);
            if (parent is null || parent == currentDir) break;
            currentDir = parent;
        }

        return fs.CombinePath(Path.GetTempPath(), DefaultLockFileName);
    }

    private static string? ResolveCommonGitDir(IFileSystem fs, string gitFilePath, string worktreePath)
    {
        try
        {
            var content = fs.ReadAllText(gitFilePath).Trim();
            const string prefix = "gitdir:";
            if (!content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var gitdirRelative = content[prefix.Length..].Trim();
            var gitdirAbs = fs.CombinePath(worktreePath, gitdirRelative);
            var normalizedGitdir = fs.GetFullPath(gitdirAbs);

            var markerIdx = normalizedGitdir.IndexOf(GitWorktreesMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIdx < 0) return null;

            var commonGitDir = normalizedGitdir[..(markerIdx + GitDirName.Length)];
            return commonGitDir;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        Release();
        return ValueTask.CompletedTask;
    }
}
