namespace Core.Memdir;

[JsonSerializable(typeof(ThinkingStoreData))]
[JsonSerializable(typeof(ThinkingEntry))]
[JsonSerializable(typeof(List<ThinkingEntry>))]
[JsonSerializable(typeof(Dictionary<string, List<ThinkingEntry>>))]
internal sealed partial class ThinkingStoreJsonContext : JsonSerializerContext;
