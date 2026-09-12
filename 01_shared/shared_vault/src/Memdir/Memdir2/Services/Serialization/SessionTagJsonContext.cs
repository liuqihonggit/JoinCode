namespace Core.Memdir;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SessionTagData))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
internal sealed partial class SessionTagJsonContext : JsonSerializerContext;
