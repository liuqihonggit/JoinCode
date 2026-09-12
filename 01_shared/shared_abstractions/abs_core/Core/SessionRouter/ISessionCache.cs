namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话级缓存接口 — 每个会话独立缓存命名空间, 会话结束自动清理
/// 缓存项本身是 CacheEntryEntity&lt;T&gt; (派生 Entity), 纳入 EntityReaper 回收
/// </summary>
public interface ISessionCache
{
    /// <summary>获取缓存值 — 不存在或已过期返回 default</summary>
    T? Get<T>(string key);

    /// <summary>设置缓存 — ttl 为 null 表示永不过期</summary>
    void Set<T>(string key, T value, TimeSpan? ttl = null);

    /// <summary>移除缓存项 — 返回是否移除成功</summary>
    bool Remove(string key);

    /// <summary>是否包含指定键(且未过期)</summary>
    bool Contains(string key);

    /// <summary>当前缓存项数量</summary>
    int Count { get; }

    /// <summary>清空所有缓存项</summary>
    void Clear();
}
