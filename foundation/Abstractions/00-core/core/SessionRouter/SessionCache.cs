namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话级缓存实现 — 基于 CacheEntryEntity&lt;T&gt;, 缓存项派生 Entity 纳入回收#F回收体系
/// 会话 Dispose 时所有 CacheEntryEntity 一起 Dispose
/// </summary>
public sealed class SessionCache : ISessionCache
{
    private readonly ConcurrentDictionary<string, Entity> _entries = new();
    private readonly ObjectId _sessionId;

    public int Count => _entries.Count;

    internal SessionCache(ObjectId sessionId)
    {
        _sessionId = sessionId;
    }

    public T? Get<T>(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return default;
        if (entry is not CacheEntryEntity<T> typed)
            return default;
        if (typed.IsExpired)
        {
            Remove(key);
            return default;
        }
        typed.OnHit();
        return typed.Value;
    }

    public void Set<T>(string key, T value, TimeSpan? ttl = null)
    {
        if (_entries.TryGetValue(key, out var existing))
            existing.Dispose();
        var entry = new CacheEntryEntity<T>(key, value, ttl, sessionId: _sessionId);
        _entries[key] = entry;
    }

    public bool Remove(string key)
    {
        if (!_entries.TryRemove(key, out var entry))
            return false;
        entry.Dispose();
        return true;
    }

    public bool Contains(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return false;
        if (entry is not CacheEntryEntity<object> typed)
            return true;
        return !typed.IsExpired;
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values)
        {
            try { entry.Dispose(); }
            catch (Exception ex) { _ = ex; }
        }
        _entries.Clear();
    }
}
