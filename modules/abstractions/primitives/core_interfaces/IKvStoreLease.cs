namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// KV 存储租约 — 短期独占会话，用完 Dispose 释放跨进程锁 + 关闭底层存储。
/// <para>租约模式（ADR 0139）：每次业务操作 Acquire 一个租约，操作期间独占，Dispose 释放。</para>
/// <para>竞争窗口 = 单次操作时间（几毫秒），不是进程生命周期。</para>
/// <para>降级内存时 IsInMemory=true，写入不持久但操作正常。</para>
/// </summary>
public interface IKvStoreLease : IKvStore, IDisposable {
    /// <summary>是否降级为内存模式（跨进程 Mutex 被占时降级，写入不持久）。</summary>
    bool IsInMemory { get; }
}

/// <summary>
/// KV 存储租约工厂 — 按需获取租约，快读快释放。
/// <para>磁盘模式：AcquireAsync 时 Open PithosDb + 获取 SemaphoreSlim(1,1) 防重入 + CrossProcessLock 跨进程互斥，Dispose 时释放。</para>
/// <para>降级模式：锁被占时返回内存租约（写入不持久，短命令够用）。</para>
/// <para>两锁联合：SemaphoreSlim 进程内防重入 + CrossProcessLock 跨进程（复用 NamedMutexMailboxLock 模式）。</para>
/// <para>替代方案（放弃）：进程级 Mutex 持有到 Dispose — 持有 = 进程生命周期，长驻 mcp server 先启动时后续 CLI 全降级。</para>
/// </summary>
public interface IKvStoreLeaseFactory {
    /// <summary>获取一个 KV 存储租约 — 非阻塞，锁被占时立即降级内存。</summary>
    Task<IKvStoreLease> AcquireAsync();
}
