namespace Core.Utils;

/// <summary>
/// 幂等条目 — 缓存的结果 + 时间戳（用于 TTL 过期）。
/// </summary>
/// <param name="Result">缓存结果（装箱后的 object）</param>
/// <param name="TimestampMs">注册时间戳（Environment.TickCount64 毫秒）</param>
internal sealed record IdempotencyEntry(object? Result, long TimestampMs);

/// <summary>
/// 幂等去重存储无锁实现 — ImmutableHamT + ImmutableInterlocked.Update CAS。
/// <para>读：引用读取原子无锁 O(1)；写：CAS 原子替换，冲突自动重试。</para>
/// <para>TTL 过期：EvictExpired 惰性清理，不自动定时清理（调用方按需调用）。</para>
/// <para>HAMT O(log₃₂ N) 优于 BCL ImmutableDictionary 平衡 BST O(log₂ N)。</para>
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore {
    private ImmutableHamT<IdempotencyKey, IdempotencyEntry> _store = ImmutableHamT<IdempotencyKey, IdempotencyEntry>.Empty;

    /// <summary>尝试注册并缓存结果</summary>
    public bool TryRegister<T>(IdempotencyKey key, T result) {
        var box = new StrongBox<bool>();
        ImmutableInterlocked.Update(ref _store, static (dict, arg) => {
            if (dict.ContainsKey(arg.key)) {
                arg.box.Value = false;
                return dict;
            }
            arg.box.Value = true;
            return dict.Add(arg.key, new IdempotencyEntry(arg.result, Environment.TickCount64));
        }, (key, result, box));
        return box.Value;
    }

    /// <summary>尝试获取缓存结果</summary>
    public bool TryGetResult<T>(IdempotencyKey key, out T? result) {
        if (!_store.TryGetValue(key, out var entry)) {
            result = default;
            return false;
        }
        if (entry.Result is null) {
            result = default;
            return true;
        }
        if (entry.Result is T typed) {
            result = typed;
            return true;
        }
        result = default;
        return false;
    }

    /// <summary>检查键是否已注册</summary>
    public bool IsRegistered(IdempotencyKey key) => _store.ContainsKey(key);

    /// <summary>移除指定键</summary>
    public void Evict(IdempotencyKey key)
        => ImmutableInterlocked.Update(ref _store, static (dict, k) => dict.Remove(k), key);

    /// <summary>清除所有已过期的条目</summary>
    public int EvictExpired(TimeSpan ttl) {
        var cutoff = Environment.TickCount64 - (long)ttl.TotalMilliseconds;
        var snapshot = _store;
        var toEvict = new List<IdempotencyKey>();
        foreach (var kvp in snapshot) {
            if (kvp.Value.TimestampMs <= cutoff)
                toEvict.Add(kvp.Key);
        }
        if (toEvict.Count == 0)
            return 0;
        ImmutableInterlocked.Update(ref _store, static (dict, keys) => dict.RemoveRange(keys), toEvict);
        return toEvict.Count;
    }
}
