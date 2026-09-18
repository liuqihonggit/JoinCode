using System.Threading;

namespace AsyncFileLock;

/// <summary>
/// 异步文件锁 — 委托 FileMailboxLock 实现跨进程互斥
/// </summary>
internal sealed class FileLock : System.IAsyncDisposable
{
    private readonly FileMailboxLock _inner;
    private int _disposed;

    /// <summary>已锁定的文件绝对路径</summary>
    public string FilePath { get; }

    private FileLock(FileMailboxLock inner)
    {
        _inner = inner;
        FilePath = inner.FilePath;
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
        var inner = await FileMailboxLock.AcquireAsync(filePath, timeout, cancellationToken, logger).ConfigureAwait(false);
        return new FileLock(inner);
    }

    /// <summary>
    /// 异步释放文件锁
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        return _inner.DisposeAsync();
    }

    /// <summary>
    /// 同步释放文件锁，语义与 DisposeAsync 等价
    /// </summary>
    internal void Release()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _inner.Release();
    }
}
