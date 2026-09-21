namespace AsyncFileLock;

/// <summary>
/// 跨进程文件邮箱锁 — 基于文件原子创建实现跨进程互斥。
/// <para>获取锁 = FileMode.CreateNew 原子创建锁文件，OS 保证只有一个进程成功。</para>
/// <para>释放锁 = 关闭流 + 删除锁文件。</para>
/// <para>进程崩溃 = 锁文件残留，超时清理（TryCleanupStaleLock）。</para>
/// <para>等待锁 = 轮询重试（线性退避），超时抛 TimeoutException。</para>
/// </summary>
public sealed class FileMailboxLock : IAsyncDisposable {
    private static readonly Lazy<IFileSystem> s_fs = new(FileSystemFactory.Create);

    private readonly Stream _lockStream;
    private readonly string _lockFilePath;
    private readonly ILogger? _logger;
    private int _disposed;

    /// <summary>已锁定的文件绝对路径</summary>
    public string FilePath { get; }

    private FileMailboxLock(Stream lockStream, string lockFilePath, string filePath, ILogger? logger) {
        _lockStream = lockStream;
        _lockFilePath = lockFilePath;
        FilePath = filePath;
        _logger = logger;
    }

    /// <summary>
    /// 异步获取指定文件路径的锁，超时未获取则抛出 TimeoutException
    /// </summary>
    /// <param name="filePath">要锁定的文件路径</param>
    /// <param name="timeout">获取锁的超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="logger">可选日志记录器</param>
    /// <returns>已获取的文件锁实例</returns>
    public static async Task<FileMailboxLock> AcquireAsync(
        string filePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        ILogger? logger = null) {
        var fs = s_fs.Value;
        var fullPath = fs.GetFullPath(filePath);
        var lockFilePath = GetLockFilePath(fullPath);
        var lockDir = Path.GetDirectoryName(lockFilePath)!;

        if (!fs.DirectoryExists(lockDir))
            fs.CreateDirectory(lockDir);

        var deadline = DateTimeOffset.UtcNow + timeout;
        var attempt = 0;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                var stream = fs.CreateStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                var content = $"{Environment.ProcessId}|{DateTimeOffset.UtcNow:O}";
                var bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                logger?.LogDebug("FileMailboxLock acquired: {FilePath} -> {LockFile}", fullPath, lockFilePath);
                return new FileMailboxLock(stream, lockFilePath, fullPath, logger);
            } catch (IOException) when (DateTimeOffset.UtcNow < deadline) {
                await TryCleanupStaleLock(fs, lockFilePath, logger).ConfigureAwait(false);
                attempt++;
                var remaining = deadline - DateTimeOffset.UtcNow;
                var delayMs = Math.Min(50 * attempt, (int)remaining.TotalMilliseconds);
                if (delayMs <= 0) break;
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException($"Failed to acquire lock for '{filePath}' within {timeout.TotalSeconds}s");
    }

    private static async ValueTask TryCleanupStaleLock(IFileSystem fs, string lockFilePath, ILogger? logger) {
        try {
            if (!fs.FileExists(lockFilePath)) return;
            var content = await fs.ReadAllText(lockFilePath).ConfigureAwait(false);
            var pipeIndex = content.IndexOf('|');
            if (pipeIndex > 0 && DateTimeOffset.TryParse(content[(pipeIndex + 1)..], out var timestamp)) {
                if (DateTimeOffset.UtcNow - timestamp > TimeSpan.FromMinutes(5))
                    fs.DeleteFile(lockFilePath);
            }
        } catch (Exception ex) {
            logger?.LogDebug(ex, "FileMailboxLock: stale lock cleanup failed for {LockFile}", lockFilePath);
        }
    }

    /// <summary>
    /// 异步释放锁 — 关闭流并删除锁文件
    /// </summary>
    public async ValueTask DisposeAsync() {
        await Release().ConfigureAwait(false);
    }

    /// <summary>
    /// 同步释放锁，语义与 DisposeAsync 等价
    /// </summary>
    internal async ValueTask Release() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try {
            await _lockStream.DisposeAsync().ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "FileMailboxLock: failed to dispose lock stream for {FilePath}", FilePath);
        }

        try {
            s_fs.Value.DeleteFile(_lockFilePath);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "FileMailboxLock: failed to delete lock file for {FilePath}", FilePath);
        }
    }

    private static string GetLockFilePath(string filePath) {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant())));
        var lockRoot = Path.Combine(Path.GetTempPath(), "JoinFileLocks");
        return Path.Combine(lockRoot, $"{hash}.lock");
    }
}