namespace Core.Utils;

public sealed class DebounceTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, Timer> _timers;
    private readonly ConcurrentDictionary<string, long> _internalWriteTimestamps;
    private bool _disposed;

    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    public int InternalWriteWindowMs { get; set; } = 5000;

    public DebounceTracker(StringComparer? comparer = null)
    {
        var c = comparer ?? StringComparer.OrdinalIgnoreCase;
        _timers = new ConcurrentDictionary<string, Timer>(c);
        _internalWriteTimestamps = new ConcurrentDictionary<string, long>(c);
    }

    public void MarkInternalWrite(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        _internalWriteTimestamps[normalizedPath] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public bool ConsumeInternalWrite(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        if (_internalWriteTimestamps.TryRemove(normalizedPath, out var timestamp))
        {
            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp;
            return elapsed < InternalWriteWindowMs;
        }
        return false;
    }

    public void ScheduleDebounce(string filePath, Action fireAction)
    {
        var interval = DebounceInterval;
        if (interval <= TimeSpan.Zero)
        {
            fireAction();
            return;
        }

        if (_timers.TryRemove(filePath, out var existingTimer))
            existingTimer.Dispose();

        _timers[filePath] = new Timer(_ =>
        {
            _timers.TryRemove(filePath, out var timer);
            timer?.Dispose();
            if (!_disposed) fireAction();
        }, null, interval, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _timers)
            kvp.Value.Dispose();
        _timers.Clear();
        _internalWriteTimestamps.Clear();
    }
}
