namespace JoinCode.Abstractions.LLM.Chat;

public sealed class DiscoveredToolSet
{
    private readonly HashSet<string> _discoveredNames = new(StringComparer.Ordinal);
    private readonly AsyncLock _lock = new();

    public async Task<IReadOnlySet<string>> GetNamesAsync(CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return new HashSet<string>(_discoveredNames, StringComparer.Ordinal);
    
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return _discoveredNames.Count;
    
    }

    public async Task<bool> IsDiscoveredAsync(string toolName, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return _discoveredNames.Contains(toolName);
    
    }

    public async Task<bool> DiscoverAsync(string toolName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return _discoveredNames.Add(toolName);
    
    }

    public async Task<int> DiscoverRangeAsync(IEnumerable<string> toolNames, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        var added = 0;
        foreach (var name in toolNames)
        {
            if (_discoveredNames.Add(name))
                added++;
        }
        return added;
    
    }

    public async Task<bool> ForgetAsync(string toolName, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return _discoveredNames.Remove(toolName);
    
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        _discoveredNames.Clear();
    
    }

    public async Task<string[]> SnapshotAsync(CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        return [.. _discoveredNames.Order()];
    
    }

    public async Task RestoreFromSnapshotAsync(string[] names, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        _discoveredNames.Clear();
        foreach (var name in names)
        {
            _discoveredNames.Add(name);
        }
    
    }
}
