namespace Core.Utils;


/// <summary>
/// 异步安全的字典封装。内部使用 <see cref="ConcurrentDictionary{TKey,TValue}"/> (CLR 内置分片锁 + lock-free),
/// 不同 key 的操作可并行, 同 key 的原子操作由 ConcurrentDictionary 保证。
/// 替代早期 Dictionary+AsyncLock 单全局锁实现, 消除不同 key 间的虚假串行。
/// </summary>
/// <remarks>
/// 并发语义注意:
/// <list type="bullet">
/// <item><see cref="AddOrUpdateAsync"/> 的 updateFactory 在并发竞争下可能被调用多次,
///   factory 必须为纯函数 (无副作用, 仅依赖输入计算输出)。</item>
/// <item>异步 factory 的 <see cref="GetOrAddAsync(TKey, Func{TKey, Task{TValue}}, CancellationToken)"/>
///   使用 per-key <see cref="AsyncLock"/> 分片锁: 不同 key 的异步 factory 并行执行,
///   同 key 的异步 factory 串行且仅执行一次。per-key 锁累积在内部字典, 不主动回收
///   (适用于有限稳定 key 集合, 如文件路径; AsyncLock 内部为 managed 资源, GC 可回收)。</item>
/// </list>
/// </remarks>
public sealed class AsyncLockedDictionary<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _dict;
    private readonly ConcurrentDictionary<TKey, AsyncLock> _keyLocks = new();

    public AsyncLockedDictionary(IEqualityComparer<TKey>? comparer = null)
    {
        _dict = new ConcurrentDictionary<TKey, TValue>(comparer ?? EqualityComparer<TKey>.Default);
        _keyLocks = new ConcurrentDictionary<TKey, AsyncLock>(comparer ?? EqualityComparer<TKey>.Default);
    }

    /// <summary>
    /// 原子 GetOrAdd (同步 factory)。不同 key 并行, 同 key 仅调用 factory 一次。
    /// </summary>
    public ValueTask<TValue> GetOrAddAsync(TKey key, Func<TKey, TValue> factory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<TValue>(_dict.GetOrAdd(key, factory));
    }

    /// <summary>
    /// 原子 GetOrAdd (异步 factory)。per-key 分片锁: 不同 key 的异步 factory 并行,
    /// 同 key 的异步 factory 串行且仅执行一次。
    /// </summary>
    public async ValueTask<TValue> GetOrAddAsync(TKey key, Func<TKey, Task<TValue>> factory, CancellationToken ct = default)
    {
        if (_dict.TryGetValue(key, out var value))
            return value;

        var keyLock = _keyLocks.GetOrAdd(key, _ => new AsyncLock());
        using var guard = await keyLock.LockAsync(ct).ConfigureAwait(false);

        if (_dict.TryGetValue(key, out value))
            return value;

        value = await factory(key).ConfigureAwait(false);
        _dict[key] = value;
        return value;
    }

    /// <summary>
    /// 原子 TryAdd。成功返回 true, 已存在返回 false。
    /// </summary>
    public ValueTask<bool> TryAddAsync(TKey key, TValue value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<bool>(_dict.TryAdd(key, value));
    }

    /// <summary>
    /// 原子 TryRemove。存在则移除并返回原值, 不存在返回 default(TValue)。
    /// </summary>
    public ValueTask<TValue?> RemoveAsync(TKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<TValue?>(_dict.TryRemove(key, out var value) ? value : default);
    }

    /// <summary>
    /// 返回当前快照 (独立副本)。读取期间不阻塞并发写入。
    /// </summary>
    public ValueTask<ReadOnlyDictionary<TKey, TValue>> SnapshotAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<ReadOnlyDictionary<TKey, TValue>>(
            new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(_dict)));
    }

    /// <summary>
    /// 原子 AddOrUpdate。新 key 时 existing=default(TValue), 已存在时 existing=当前值。
    /// updateFactory 必须为纯函数 (并发竞争下可能被调用多次)。
    /// </summary>
    public ValueTask<TValue> AddOrUpdateAsync(TKey key, Func<TKey, TValue?, TValue> updateFactory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = _dict.AddOrUpdate(
            key,
            k => updateFactory(k, default),
            (k, existing) => updateFactory(k, existing));
        return new ValueTask<TValue>(result);
    }
}
