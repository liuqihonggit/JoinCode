namespace JoinCode.Transport;

public sealed class BufferedChannel : IDisposable
{
    private readonly List<string> _buffer = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _consumedIndex;

    public async Task AddAsync(string line, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _buffer.Add(line);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> GetAllAsync(TimeSpan lockTimeout, CancellationToken ct = default)
    {
        await _lock.WaitAsync(lockTimeout, ct).ConfigureAwait(false);
        try
        {
            return string.Join("\n", _buffer);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> GetIncrementalAsync(TimeSpan lockTimeout, CancellationToken ct = default)
    {
        await _lock.WaitAsync(lockTimeout, ct).ConfigureAwait(false);
        try
        {
            if (_consumedIndex >= _buffer.Count)
                return string.Empty;

            var result = string.Join("\n", _buffer[_consumedIndex..]);
            _consumedIndex = _buffer.Count;
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(TimeSpan lockTimeout, CancellationToken ct = default)
    {
        await _lock.WaitAsync(lockTimeout, ct).ConfigureAwait(false);
        try
        {
            _buffer.Clear();
            _consumedIndex = 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> TryPredicateAsync(Func<string, bool> predicate, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return predicate(string.Join("\n", _buffer));
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();
}
