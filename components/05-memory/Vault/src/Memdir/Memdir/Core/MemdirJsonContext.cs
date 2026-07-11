
namespace Core.Memdir;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(MemoryEntry))]
[JsonSerializable(typeof(List<MemoryEntry>))]
[JsonSerializable(typeof(SearchHistoryEntry))]
[JsonSerializable(typeof(List<SearchHistoryEntry>))]
[JsonSerializable(typeof(PastContextSection))]
public partial class MemdirJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(List<MemoryEntry>))]
[JsonSerializable(typeof(SearchHistoryEntry))]
[JsonSerializable(typeof(List<SearchHistoryEntry>))]
[JsonSerializable(typeof(PastContextSection))]
public partial class MemdirIndentedJsonContext : JsonSerializerContext;
