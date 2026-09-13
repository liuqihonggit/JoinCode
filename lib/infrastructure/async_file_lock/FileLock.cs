
namespace AsyncFileLock;

/// <summary>
/// 异步文件锁 — 基于跨进程互斥量实现，内部类不对外暴露
/// </summary>
internal sealed class FileLock : System.IAsyncDisposable
{
    private readonly AsyncCrossProcessMutex _mutex;
    private AsyncCrossProcessMutex.LockReleaser? _releaser;
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>已锁定的文件绝对路径</summary>
    public string FilePath { get; }

    private FileLock(string filePath, AsyncCrossProcessMutex mutex, AsyncCrossProcessMutex.LockReleaser releaser, ILogger? logger = null)
    {
        FilePath = filePath;
        _mutex = mutex;
        _releaser = releaser;
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
    public static async Task<FileLock> AcquireAsync(
        string filePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        var fullPath = Path.GetFullPath(filePath);
        var mutexName = GetMutexName(fullPath);

        var mutex = new AsyncCrossProcessMutex(mutexName);
        try
        {
            var releaser = await mutex.TryEnterAsync(timeout).ConfigureAwait(false);
            if (releaser == null)
            {
                mutex.Dispose();
                throw new TimeoutException(
                    $"Failed to acquire lock for '{filePath}' within {timeout.TotalSeconds}s");
            }

            return new FileLock(fullPath, mutex, releaser.Value, logger);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 异步释放文件锁，释放跨进程互斥量
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

        if (_releaser.HasValue)
        {
            try { _releaser.Value.Dispose(); } catch (Exception ex) { _logger?.LogWarning(ex, "FileLock: failed to dispose releaser"); }
            _releaser = null;
        }

        try { _mutex.Dispose(); } catch (Exception ex) { _logger?.LogWarning(ex, "FileLock: failed to dispose mutex"); }
    }

    /// <summary>
    /// 同步释放文件锁，语义与 DisposeAsync 等价
    /// </summary>
    internal void Release()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

        if (_releaser.HasValue)
        {
            try { _releaser.Value.Dispose(); } catch (Exception ex) { _logger?.LogWarning(ex, "FileLock: failed to dispose releaser on release"); }
            _releaser = null;
        }

        try { _mutex.Dispose(); } catch (Exception ex) { _logger?.LogWarning(ex, "FileLock: failed to dispose mutex on release"); }
    }

    private static string GetMutexName(string filePath)
    {
        var fullPath = filePath.ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        return $"Global\\AsyncFileLock_{hash}";
    }
}
