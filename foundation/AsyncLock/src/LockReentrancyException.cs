namespace Core.Utils;

/// <summary>
/// 同一线程对同一把 <see cref="AsyncLock"/> 重入时抛出。
/// AsyncLock 基于 SemaphoreSlim(1,1)，不支持重入 — 持锁时再次获取会永久阻塞（等自己释放）。
/// 此异常已不再使用：CheckReentrancy 已移除，ThreadId 在 async/await 下因线程池复用不可靠。
/// 保留类型供未来可能的同步专用重入检测使用。
/// </summary>
public sealed class LockReentrancyException : Exception
{
    /// <summary>重入的锁名称。</summary>
    public string LockName { get; }

    /// <summary>重入的锁 ID。</summary>
    public int LockId { get; }

    /// <summary>重入的线程 ID。</summary>
    public int ThreadId { get; }

    public LockReentrancyException(string lockName, int lockId, int threadId, string message)
        : base(message)
    {
        LockName = lockName;
        LockId = lockId;
        ThreadId = threadId;
    }
}
