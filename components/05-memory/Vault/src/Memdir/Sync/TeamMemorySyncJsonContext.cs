
namespace Memdir.Sync;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemorySyncEvent))]
[JsonSerializable(typeof(List<MemorySyncEvent>))]
[JsonSerializable(typeof(SyncFileEntry))]
[JsonSerializable(typeof(List<SyncFileEntry>))]
[JsonSerializable(typeof(TeamSyncStatus))]
[JsonSerializable(typeof(List<TeamSyncStatus>))]
[JsonSerializable(typeof(TeamMemoryConflict))]
[JsonSerializable(typeof(List<TeamMemoryConflict>))]
[JsonSerializable(typeof(string))]
public partial class TeamMemorySyncJsonContext : JsonSerializerContext;
