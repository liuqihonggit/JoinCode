
namespace Memdir.Sync;

public sealed class MemorySyncEvent
{
    public required string EventId { get; init; }
    public required string FilePath { get; init; }
    public required SyncEventType Type { get; init; }
    public required DateTime Timestamp { get; init; }
    public string? ContentHash { get; init; }
    public SyncConflictResolution? ConflictResolution { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum SyncEventType
{
    [EnumValue("localChanged")]
    LocalChanged,
    [EnumValue("remoteChanged")]
    RemoteChanged,
    [EnumValue("synced")]
    Synced,
    [EnumValue("conflictDetected")]
    ConflictDetected,
    [EnumValue("conflictResolved")]
    ConflictResolved,
    [EnumValue("error")]
    Error
}
