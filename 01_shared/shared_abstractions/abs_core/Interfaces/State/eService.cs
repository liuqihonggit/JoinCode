namespace JoinCode.Abstractions.Interfaces;

public interface ICacheService
{
    T? Get<T>(string key);
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    void Set<T>(string key, T value, TimeSpan? expiration = null);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    bool Remove(string key);
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
    bool ContainsKey(string key);
    Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default);
    void Clear();
    Task ClearAsync(CancellationToken cancellationToken = default);
}
