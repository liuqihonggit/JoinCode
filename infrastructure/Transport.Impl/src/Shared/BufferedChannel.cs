namespace JoinCode.Transport;

public sealed class BufferedChannel : IDisposable
{
    private readonly List<string> _buffer = new();
    private readonly AsyncLock _lock = new();
    private int _consumedIndex;

    public async Task AddAsync(string line, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException("锁等待超时");

        _buffer.Add(line);
    
    }

    public async Task<string> GetAllAsync(TimeSpan lockTimeout, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false)
            ?? throw new TimeoutException($"BufferedChannel 锁等待超时 {lockTimeout}");

        return string.Join("\n", _buffer);
    
    }

    public async Task<string> GetIncrementalAsync(TimeSpan lockTimeout, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false)
            ?? throw new TimeoutException($"BufferedChannel 锁等待超时 {lockTimeout}");

        if (_consumedIndex >= _buffer.Count)
            return string.Empty;

        var result = string.Join("\n", _buffer[_consumedIndex..]);
        _consumedIndex = _buffer.Count;
        return result;
    
    }

    public async Task ClearAsync(TimeSpan lockTimeout, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false)
            ?? throw new TimeoutException($"BufferedChannel 锁等待超时 {lockTimeout}");

        _buffer.Clear();
        _consumedIndex = 0;
    
    }

    public async Task<bool> TryPredicateAsync(Func<string, bool> predicate, CancellationToken ct = default)
    {
        using var guard = await _lock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException("锁等待超时");

        return predicate(string.Join("\n", _buffer));
    
    }

    public void Dispose() => _lock.Dispose();
}
