
namespace AsyncFileLock;

// 内部类，不对外暴露
internal sealed class FileLock : System.IAsyncDisposable
{
    private readonly AsyncCrossProcessMutex _mutex;
    private AsyncCrossProcessMutex.LockReleaser? _releaser;
    private bool _disposed;

    public string FilePath { get; }

    private FileLock(string filePath, AsyncCrossProcessMutex mutex, AsyncCrossProcessMutex.LockReleaser releaser)
    {
        FilePath = filePath;
        _mutex = mutex;
        _releaser = releaser;
    }

    public static async Task<FileLock> AcquireAsync(
        string filePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
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

            return new FileLock(fullPath, mutex, releaser.Value);
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
            try { _releaser.Value.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"FileLock: failed to dispose releaser: {ex.Message}"); }
            _releaser = null;
        }

        try { _mutex.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"FileLock: failed to dispose mutex: {ex.Message}"); }
    }

    internal void Release()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

        if (_releaser.HasValue)
        {
            try { _releaser.Value.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"FileLock: failed to dispose releaser on release: {ex.Message}"); }
            _releaser = null;
        }

        try { _mutex.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"FileLock: failed to dispose mutex on release: {ex.Message}"); }
    }

    private static string GetMutexName(string filePath)
    {
        var fullPath = filePath.ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        return $"Global\\AsyncFileLock_{hash}";
    }
}
