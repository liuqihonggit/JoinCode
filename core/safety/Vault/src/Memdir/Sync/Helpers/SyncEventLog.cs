namespace Memdir.Sync.Helpers;

internal sealed class SyncEventLog
{
    private readonly ConcurrentQueue<MemorySyncEvent> _syncHistory = new();
    private const int MaxSyncHistory = 1000;

    internal ConcurrentQueue<MemorySyncEvent> History => _syncHistory;

    internal void Enqueue(MemorySyncEvent syncEvent)
    {
        _syncHistory.Enqueue(syncEvent);
        while (_syncHistory.Count > MaxSyncHistory)
        {
            _syncHistory.TryDequeue(out _);
        }
    }

    internal IEnumerable<MemorySyncEvent> GetRecent(int limit)
        => _syncHistory.OrderByDescending(e => e.Timestamp).Take(limit);
}
