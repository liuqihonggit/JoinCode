namespace Core.Utils;

using System.Collections.ObjectModel;

public sealed class AsyncLockedDictionary<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _dict;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AsyncLockedDictionary(IEqualityComparer<TKey>? comparer = null)
    {
        _dict = new Dictionary<TKey, TValue>(comparer ?? EqualityComparer<TKey>.Default);
    }

    public async ValueTask<TValue> GetOrAddAsync(TKey key, Func<TKey, TValue> factory, CancellationToken ct = default)
    {
        using var guard = await AsyncLockGuard.AcquireAsync(_lock, ct).ConfigureAwait(false);

        if (_dict.TryGetValue(key, out var value))
            return value;

        value = factory(key);
        _dict[key] = value;
        return value;
    }

    public async ValueTask<TValue> GetOrAddAsync(TKey key, Func<TKey, Task<TValue>> factory, CancellationToken ct = default)
    {
        using var guard = await AsyncLockGuard.AcquireAsync(_lock, ct).ConfigureAwait(false);

        if (_dict.TryGetValue(key, out var value))
            return value;

        value = await factory(key).ConfigureAwait(false);
        _dict[key] = value;
        return value;
    }

    public async ValueTask<bool> TryAddAsync(TKey key, TValue value, CancellationToken ct = default)
    {
        using var guard = await AsyncLockGuard.AcquireAsync(_lock, ct).ConfigureAwait(false);
        return _dict.TryAdd(key, value);
    }

    public async ValueTask<TValue?> RemoveAsync(TKey key, CancellationToken ct = default)
    {
        using var guard = await AsyncLockGuard.AcquireAsync(_lock, ct).ConfigureAwait(false);

        if (_dict.TryGetValue(key, out var value))
        {
            _dict.Remove(key);
            return value;
        }

        return default;
    }

    public async ValueTask<ReadOnlyDictionary<TKey, TValue>> SnapshotAsync(CancellationToken ct = default)
    {
        using var guard = await AsyncLockGuard.AcquireAsync(_lock, ct).ConfigureAwait(false);
        return new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(_dict));
    }

    public async ValueTask<TValue> AddOrUpdateAsync(TKey key, Func<TKey, TValue?, TValue> updateFactory, CancellationToken ct = default)
    {
        using var guard = await AsyncLockGuard.AcquireAsync(_lock, ct).ConfigureAwait(false);

        _dict.TryGetValue(key, out var existing);
        var updated = updateFactory(key, existing); // existing may be default(TValue) for new keys
        _dict[key] = updated;
        return updated;
    }
}
