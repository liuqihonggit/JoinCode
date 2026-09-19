
namespace Memdir.Sync;

/// <summary>
/// 团队记忆同步模块的 JSON 序列化上下文 — 为 AOT 编译预生成 MemorySyncEvent/SyncFileEntry/TeamSyncStatus/TeamMemoryConflict 等类型的元数据。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
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