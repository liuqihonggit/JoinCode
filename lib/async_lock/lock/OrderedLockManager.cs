namespace Core.Utils;

/// <summary>
/// 区间锁 / 多资源排序锁 — 正序加锁、逆序解锁,避免死锁。
/// <para>用于 Actor 外部协调层:多个 Actor 需要协调操作时,用排序锁保证协调顺序。</para>
/// <para>Actor 内部不用此锁(Actor 用 Channel 消息传递,无锁模型)。</para>
/// <para><b>死锁预防</b>:所有调用者对同一组资源按 ID 升序加锁,破坏"循环等待"条件。</para>
/// </summary>
public sealed class OrderedLockManager : IDisposable {
    private readonly Dictionary<long, object> _locks = new();
    private readonly object _dictLock = new();
    private int _disposed;

    private object GetLockObject(long id) {
        lock (_dictLock) {
            if (!_locks.TryGetValue(id, out var obj)) {
                obj = new object();
                _locks[id] = obj;
            }
            return obj;
        }
    }

    /// <summary>
    /// 对一组资源 ID 加锁(区间锁)。
    /// <para>内部去重、排序后正序加锁,返回 IDisposable,Dispose 时逆序解锁。</para>
    /// </summary>
    /// <param name="ids">需要加锁的资源 ID 集合</param>
    /// <returns>解锁令牌 — Dispose 时逆序释放</returns>
    public IDisposable LockRange(IEnumerable<long> ids) {
        ArgumentNullException.ThrowIfNull(ids);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var ordered = ids.Distinct().OrderBy(static x => x).ToArray();
        var acquired = new List<object>(ordered.Length);
        try {
            foreach (var id in ordered) {
                var lockObj = GetLockObject(id);
                Monitor.Enter(lockObj);
                acquired.Add(lockObj);
            }
            return new UnlockToken(acquired);
        } catch {
            for (var i = acquired.Count - 1; i >= 0; i--) {
                Monitor.Exit(acquired[i]);
            }
            throw;
        }
    }

    /// <summary>
    /// 单个资源加锁的便捷方法
    /// </summary>
    public IDisposable Lock(long id) => LockRange(new[] { id });

    /// <inheritdoc/>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_dictLock) {
            _locks.Clear();
        }
    }

    private sealed class UnlockToken : IDisposable {
        private readonly List<object> _acquired;
        private int _disposed;

        /// <summary>构造解锁令牌。</summary>
        /// <param name="acquired">按获取顺序记录的已加锁对象列表。</param>
        public UnlockToken(List<object> acquired) => _acquired = acquired;

        /// <summary>释放资源。</summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            for (var i = _acquired.Count - 1; i >= 0; i--) {
                Monitor.Exit(_acquired[i]);
            }
            _acquired.Clear();
        }
    }
}