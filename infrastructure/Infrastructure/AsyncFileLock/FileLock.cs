
namespace AsyncFileLock;

// 内部类，不对外暴露
internal sealed class FileLock : System.IAsyncDisposable
{
    private readonly AsyncCrossProcessMutex _mutex;
    private AsyncCrossProcessMutex.LockReleaser? _releaser;
    private readonly ILogger? _logger;
    private bool _disposed;

    public string FilePath { get; }

    private FileLock(string filePath, AsyncCrossProcessMutex mutex, AsyncCrossProcessMutex.LockReleaser releaser, ILogger? logger = null)
    {
        FilePath = filePath;
        _mutex = mutex;
        _releaser = releaser;
        _logger = logger;
    }

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
