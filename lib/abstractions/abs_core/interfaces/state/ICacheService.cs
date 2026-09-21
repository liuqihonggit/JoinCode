namespace JoinCode.Abstractions.Interfaces;

public interface ICacheService {
    /// <summary>获取缓存值。</summary>
    T? Get<T>(string key);
    /// <summary>异步获取缓存值。</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    /// <summary>设置缓存值。</summary>
    void Set<T>(string key, T value, TimeSpan? expiration = null);
    /// <summary>异步设置缓存值。</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    /// <summary>移除缓存项。</summary>
    bool Remove(string key);
    /// <summary>异步移除缓存项。</summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
    /// <summary>判断是否包含指定键。</summary>
    bool ContainsKey(string key);
    /// <summary>异步判断是否包含指定键。</summary>
    Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default);
    /// <summary>清空缓存。</summary>
    void Clear();
    /// <summary>异步清空缓存。</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}