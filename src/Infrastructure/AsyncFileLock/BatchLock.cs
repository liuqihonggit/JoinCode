namespace AsyncFileLock;

/// <summary>
/// 批量文件锁，确保原子性获取多个文件的锁，防止死锁
/// </summary>
public sealed class BatchLock : IAsyncDisposable
{
    private readonly IReadOnlyList<FileLock> _locks;
    private bool _disposed;

    /// <summary>
    /// 已锁定的文件路径列表
    /// </summary>
    public IReadOnlyList<string> FilePaths { get; }

    internal BatchLock(IReadOnlyList<FileLock> locks)
    {
        _locks = locks;
        FilePaths = locks.Select(l => l.FilePath).ToList();
    }

    /// <summary>
    /// 释放所有锁（按相反顺序）
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

        await Task.WhenAll(_locks.Select(l => l.DisposeAsync().AsTask())).ConfigureAwait(false);
    }
}

/// <summary>
/// 批量锁获取结果
/// </summary>
public sealed class BatchLockResult
{
    /// <summary>
    /// 是否成功获取所有锁
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// 批量锁对象（仅当 Success 为 true 时有效）
    /// </summary>
    public BatchLock? Lock { get; }

    /// <summary>
    /// 错误信息（仅当 Success 为 false 时有效）
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 获取锁所用时间
    /// </summary>
    public TimeSpan? AcquisitionTime { get; }

    private BatchLockResult(bool success, BatchLock? batchLock, string? errorMessage, TimeSpan? acquisitionTime)
    {
        Success = success;
        Lock = batchLock;
        ErrorMessage = errorMessage;
        AcquisitionTime = acquisitionTime;
    }

    internal static BatchLockResult SuccessResult(BatchLock batchLock, TimeSpan acquisitionTime)
    {
        return new BatchLockResult(true, batchLock, null, acquisitionTime);
    }

    internal static BatchLockResult ErrorResult(string errorMessage)
    {
        return new BatchLockResult(false, null, errorMessage, null);
    }

    internal static BatchLockResult TimeoutResult(string filePath, TimeSpan elapsed)
    {
        return new BatchLockResult(false, null, $"Timeout acquiring lock for '{filePath}' after {elapsed.TotalSeconds}s", elapsed);
    }
}

/// <summary>
/// 文件锁服务，提供批量获取文件锁的功能
/// </summary>
public static class FileLockService
{
    /// <summary>
    /// 批量获取文件锁
    /// </summary>
    /// <param name="filePaths">要锁定的文件路径列表</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="clock">时钟服务</param>
    /// <returns>批量锁获取结果</returns>
    /// <remarks>
    /// 此方法会自动对文件路径进行排序，确保按统一顺序获取锁，防止死锁。
    /// 建议始终使用此方法而不是单独获取多个文件的锁。
    /// </remarks>
    public static async Task<BatchLockResult> AcquireBatchAsync(
        IReadOnlyList<string> filePaths,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        IClockService? clock = null)
    {
        var c = clock ?? SystemClockService.Instance;
        if (filePaths.Count == 0)
        {
            return BatchLockResult.ErrorResult("No files specified");
        }

        // 去重并排序（防止死锁的关键）
        var sorted = filePaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var acquired = new List<FileLock>();
        var startTime = c.GetUtcNow();

        try
        {
            foreach (var filePath in sorted)
            {
                var remaining = timeout - (c.GetUtcNow() - startTime);
                if (remaining <= TimeSpan.Zero)
                {
                    return BatchLockResult.TimeoutResult(filePath, c.GetUtcNow() - startTime);
                }

                try
                {
                    var fileLock = await FileLock.AcquireAsync(filePath, remaining, cancellationToken)
                        .ConfigureAwait(false);
                    acquired.Add(fileLock);
                }
                catch (TimeoutException)
                {
                    return BatchLockResult.TimeoutResult(filePath, c.GetUtcNow() - startTime);
                }
                catch (OperationCanceledException)
                {
                    ReleaseAllReverse(acquired);
                    throw;
                }
            }

            var batchLock = new BatchLock(acquired);
            return BatchLockResult.SuccessResult(batchLock, c.GetUtcNow() - startTime);
        }
        catch (OperationCanceledException)
        {
            ReleaseAllReverse(acquired);
            throw;
        }
        catch (Exception ex)
        {
            ReleaseAllReverse(acquired);
            return BatchLockResult.ErrorResult($"Failed to acquire batch lock: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取单个文件的锁（内部使用批量锁）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="clock">时钟服务</param>
    /// <returns>批量锁获取结果</returns>
    public static Task<BatchLockResult> AcquireAsync(
        string filePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        IClockService? clock = null)
    {
        return AcquireBatchAsync([filePath], timeout, cancellationToken, clock);
    }

    private static void ReleaseAllReverse(List<FileLock> acquired)
    {
        for (var i = acquired.Count - 1; i >= 0; i--)
        {
            try { acquired[i].Release(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"BatchLock: failed to release lock during rollback: {ex.Message}"); }
        }
    }
}
