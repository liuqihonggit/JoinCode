namespace State;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JoinCode.Abstractions.Interfaces.AgentMetadata))]
public sealed partial class AgentMetadataJsonContext : JsonSerializerContext;
