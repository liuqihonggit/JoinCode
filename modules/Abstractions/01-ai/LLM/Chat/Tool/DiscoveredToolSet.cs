namespace JoinCode.Abstractions.LLM.Chat;

public sealed class DiscoveredToolSet
{
    private readonly HashSet<string> _discoveredNames = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlySet<string>> GetNamesAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return new HashSet<string>(_discoveredNames, StringComparer.Ordinal);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _discoveredNames.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> IsDiscoveredAsync(string toolName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _discoveredNames.Contains(toolName);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DiscoverAsync(string toolName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _discoveredNames.Add(toolName);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> DiscoverRangeAsync(IEnumerable<string> toolNames, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var added = 0;
            foreach (var name in toolNames)
            {
                if (_discoveredNames.Add(name))
                    added++;
            }
            return added;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ForgetAsync(string toolName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _discoveredNames.Remove(toolName);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _discoveredNames.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string[]> SnapshotAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return [.. _discoveredNames.Order()];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RestoreFromSnapshotAsync(string[] names, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _discoveredNames.Clear();
            foreach (var name in names)
            {
                _discoveredNames.Add(name);
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
