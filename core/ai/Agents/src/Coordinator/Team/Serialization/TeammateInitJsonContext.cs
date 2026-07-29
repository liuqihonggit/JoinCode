namespace Core.Agents.Coordinator;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(TeammateIdleNotification))]
internal sealed partial class TeammateInitJsonContext : JsonSerializerContext;
