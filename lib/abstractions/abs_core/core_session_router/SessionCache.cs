// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话级缓存实现 — 基于 CacheEntryEntity&lt;T&gt;, 缓存项派生 Entity 纳入回收#F回收体系
/// 会话 Dispose 时所有 CacheEntryEntity 一起 Dispose
/// </summary>
public sealed class SessionCache : ISessionCache {
    private volatile ImmutableHamT<string, Entity> _entries = ImmutableHamT<string, Entity>.Empty;
    private readonly ObjectId _sessionId;

    /// <summary>获取缓存项数量。</summary>
    public int Count => _entries.Count;

    internal SessionCache(ObjectId sessionId) {
        _sessionId = sessionId;
    }

    /// <summary>获取指定键的缓存值。</summary>
    public T? Get<T>(string key) {
        if (!_entries.TryGetValue(key, out var entry))
            return default;
        if (entry is not CacheEntryEntity<T> typed)
            return default;
        if (typed.IsExpired) {
            _ = RemoveAsync(key);
            return default;
        }
        typed.OnHit();
        return typed.Value;
    }

    /// <summary>设置指定键的缓存值 — 手写 CAS 循环替代 ImmutableInterlocked.Update(volatile 字段触发 CS0420)。</summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) {
        if (_entries.TryGetValue(key, out var existing))
            await existing.DisposeAsync().ConfigureAwait(false);
        while (true) {
            var current = _entries;
            var updated = current.SetItem(key, new CacheEntryEntity<T>(key, value, ttl, sessionId: _sessionId));
            if (Interlocked.CompareExchange(ref _entries, updated, current) == current) break;
        }
    }

    /// <summary>移除指定键的缓存项 — 手写 CAS 循环替代 ImmutableInterlocked.Update(volatile 字段触发 CS0420)。</summary>
    public async Task<bool> RemoveAsync(string key) {
        Entity? removed = null;
        while (true) {
            var current = _entries;
            if (!current.TryGetValue(key, out var e)) break;
            var updated = current.Remove(key);
            if (Interlocked.CompareExchange(ref _entries, updated, current) == current) { removed = e; break; }
        }
        if (removed is null) return false;
        await removed.DisposeAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>判断是否包含指定键的缓存项。</summary>
    public bool Contains(string key) {
        if (!_entries.TryGetValue(key, out var entry))
            return false;
        if (entry is not CacheEntryEntity<object> typed)
            return true;
        return !typed.IsExpired;
    }

    /// <summary>清空所有缓存项 — 手写 CAS 循环替代 Interlocked.Exchange(volatile 字段触发 CS0420),保留旧值用于 Dispose。</summary>
    public async Task ClearAsync() {
        ImmutableHamT<string, Entity> snapshot = default!;
        while (true) {
            var current = _entries;
            if (Interlocked.CompareExchange(ref _entries, ImmutableHamT<string, Entity>.Empty, current) == current) { snapshot = current; break; }
        }
        foreach (var entry in snapshot.Values) {
            try { await entry.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { _ = ex; }
        }
    }
}
