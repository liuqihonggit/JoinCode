namespace Core.Agents.Coordinator;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TeammateIdleNotification))]
internal sealed partial class TeammateInitJsonContext : JsonSerializerContext;
