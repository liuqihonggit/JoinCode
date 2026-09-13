
namespace Core.Memdir;

/// <summary>
/// Memdir JSON 序列化上下文 — 紧凑格式,用于持久化记忆条目、搜索历史等数据
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MemoryEntry))]
[JsonSerializable(typeof(List<MemoryEntry>))]
[JsonSerializable(typeof(SearchHistoryEntry))]
[JsonSerializable(typeof(List<SearchHistoryEntry>))]
[JsonSerializable(typeof(PastContextSection))]
[JsonSerializable(typeof(List<TeamMemoryPath>))]
public partial class MemdirJsonContext : JsonSerializerContext;

/// <summary>
/// Memdir 缩进 JSON 序列化上下文 — 带缩进格式,用于人类可读的场景
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<MemoryEntry>))]
[JsonSerializable(typeof(SearchHistoryEntry))]
[JsonSerializable(typeof(List<SearchHistoryEntry>))]
[JsonSerializable(typeof(PastContextSection))]
public partial class MemdirIndentedJsonContext : JsonSerializerContext;
