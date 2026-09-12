namespace Core.Memdir;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ThinkingStoreData))]
[JsonSerializable(typeof(ThinkingEntry))]
[JsonSerializable(typeof(List<ThinkingEntry>))]
[JsonSerializable(typeof(Dictionary<string, List<ThinkingEntry>>))]
internal sealed partial class ThinkingStoreJsonContext : JsonSerializerContext;
