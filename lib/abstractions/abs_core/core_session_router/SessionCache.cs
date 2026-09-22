namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话级缓存实现 — 基于 CacheEntryEntity&lt;T&gt;, 缓存项派生 Entity 纳入回收#F回收体系
/// 会话 Dispose 时所有 CacheEntryEntity 一起 Dispose
/// </summary>
public sealed class SessionCache : ISessionCache {
    private readonly ConcurrentDictionary<string, Entity> _entries = new();
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

    /// <summary>设置指定键的缓存值。</summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) {
        if (_entries.TryGetValue(key, out var existing))
            await existing.DisposeAsync().ConfigureAwait(false);
        var entry = new CacheEntryEntity<T>(key, value, ttl, sessionId: _sessionId);
        _entries[key] = entry;
    }

    /// <summary>移除指定键的缓存项。</summary>
    public async Task<bool> RemoveAsync(string key) {
        if (!_entries.TryRemove(key, out var entry))
            return false;
        await entry.DisposeAsync().ConfigureAwait(false);
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

    /// <summary>清空所有缓存项。</summary>
    public async Task ClearAsync() {
        foreach (var entry in _entries.Values) {
            try { await entry.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { _ = ex; }
        }
        _entries.Clear();
    }
}