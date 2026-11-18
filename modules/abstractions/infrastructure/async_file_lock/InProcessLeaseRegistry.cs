namespace AsyncFileLock;

/// <summary>
/// 进程内租约登记表 — 用 <see cref="ConcurrentDictionary{TKey,TValue}"/> 原子占位表达"本进程已持有"，
/// 替代原 SemaphoreSlim(1,1) 防重入；等待方按退避轮询占位，无监视器/无信号量。
/// <para>跨进程互斥由 <see cref="CrossProcessLock"/> 的原子文件租约负责，本表只消除同进程内的无效跨进程竞争。</para>
/// </summary>
internal static class InProcessLeaseRegistry {
    private static readonly ConcurrentDictionary<string, byte> Claims = new(StringComparer.Ordinal);

    /// <summary>非阻塞占位 — 已占用立即返回 false（调用方按业务降级）。</summary>
    public static bool TryClaim(string key) => Claims.TryAdd(key, 0);

    /// <summary>释放占位 — 幂等。</summary>
    public static void Release(string key) => Claims.TryRemove(key, out _);

    /// <summary>
    /// 阻塞等待占位 — 超时抛 <see cref="TimeoutException"/>；<see cref="TimeSpan.Zero"/> 只尝试一次。
    /// </summary>
    public static async ValueTask ClaimAsync(string key, TimeSpan timeout, CancellationToken ct) {
        if (TryClaim(key)) return;

        var deadline = timeout == Timeout.InfiniteTimeSpan ? DateTime.MaxValue : DateTime.UtcNow + timeout;
        var pollMs = 10;
        while (true) {
            ct.ThrowIfCancellationRequested();
            if (timeout <= TimeSpan.Zero || DateTime.UtcNow >= deadline) {
                throw new TimeoutException($"进程内租约被占用 '{key}'（{timeout.TotalSeconds}s 内未释放）");
            }
            await Task.Delay(pollMs, ct).ConfigureAwait(false);
            pollMs = Math.Min(pollMs * 2, 50);
            if (TryClaim(key)) return;
        }
    }
}
