// JCC9002: 跨进程锁是底层基础设施，直接使用 FileStream 是设计意图（flock 需要真实文件句柄）
// JCC9006: FileShare.None 是互斥锁的语义要求，不能用 ReadWrite 否则锁失效
#pragma warning disable JCC9002, JCC9006
namespace AsyncFileLock;

/// <summary>
/// 跨进程锁 — 平台自适应：Windows 用 NamedMutex（内核对象），Linux 用 FileStream+FileShare.None（flock）。
/// <para>Windows: NamedMutex 内核对象，OS 保证原子性，进程崩溃自动回收。</para>
/// <para>Linux: FileStream(FileShare.None) 映射到 flock(LOCK_EX)，POSIX 标准跨进程互斥。</para>
/// <para>NamedMutex 在 Linux 上用 Global\ 前缀 + Dispose 销毁共享内存段行为不可控，改用 flock 更可靠。</para>
/// </summary>
internal sealed class CrossProcessLock : IDisposable {
    private readonly Mutex? _mutex;
    private readonly FileStream? _fileStream;
    private readonly bool _isLinux;
    private int _disposed;

    private CrossProcessLock(Mutex mutex) {
        _mutex = mutex;
        _isLinux = false;
    }

    private CrossProcessLock(FileStream fileStream) {
        _fileStream = fileStream;
        _isLinux = true;
    }

    /// <summary>
    /// 异步获取跨进程锁，超时未获取则抛出 TimeoutException
    /// </summary>
    public static async Task<CrossProcessLock> AcquireAsync(string key, TimeSpan timeout, CancellationToken ct) {
        if (OperatingSystem.IsLinux()) {
            return await AcquireFlockAsync(key, timeout, ct).ConfigureAwait(false);
        }
        return await AcquireMutexAsync(key, timeout, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Windows: 命名 Mutex — Global 作用域优先，权限不足回退 Local
    /// </summary>
    private static async Task<CrossProcessLock> AcquireMutexAsync(string key, TimeSpan timeout, CancellationToken ct) {
        var mutexName = "jcc_mailbox_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        Mutex mutex;
        try {
            mutex = new Mutex(initiallyOwned: false, name: "Global\\" + mutexName, createdNew: out _);
        } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            mutex = new Mutex(initiallyOwned: false, name: "Local\\" + mutexName, createdNew: out _);
        }
        try {
            var acquired = await Task.Run(() => {
                try { return mutex.WaitOne(timeout); }
                catch (AbandonedMutexException) { return true; }
            }, ct).ConfigureAwait(false);
            if (!acquired) {
                mutex.Dispose();
                throw new TimeoutException($"Failed to acquire cross-process lock for '{key}' within {timeout.TotalSeconds}s");
            }
            return new CrossProcessLock(mutex);
        } catch {
            mutex.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Linux: FileStream + FileShare.None — flock(LOCK_EX) 实现跨进程互斥
    /// </summary>
    private static async Task<CrossProcessLock> AcquireFlockAsync(string key, TimeSpan timeout, CancellationToken ct) {
        var lockPath = key + ".lck";
        var deadline = DateTime.UtcNow + timeout;
        var delayMs = 10;
        while (true) {
            ct.ThrowIfCancellationRequested();
            try {
                var fs = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new CrossProcessLock(fs);
            } catch (IOException) {
                if (DateTime.UtcNow >= deadline) {
                    throw new TimeoutException($"Failed to acquire cross-process lock for '{key}' within {timeout.TotalSeconds}s");
                }
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                delayMs = Math.Min(delayMs * 2, 100);
            }
        }
    }

    /// <summary>
    /// 释放跨进程锁资源 — Linux 释放文件句柄，Windows 释放 Mutex 所有权再 Dispose
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_isLinux) {
            _fileStream?.Dispose();
        } else {
            try { _mutex?.ReleaseMutex(); } catch (ApplicationException ex) { System.Diagnostics.Trace.WriteLine($"ReleaseMutex skipped: {ex.Message}"); }
            _mutex?.Dispose();
        }
    }
}
