namespace JoinCode.Abstractions.LLM.Chat;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SessionMeta))]
public sealed partial class SessionMetaJsonContext : JsonSerializerContext;