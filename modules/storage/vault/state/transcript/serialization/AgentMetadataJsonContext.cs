namespace State;

/// <summary>
/// AgentMetadata 的 JSON 序列化上下文 — 为 AgentTranscriptService 提供 AOT 友好的序列化支持
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JoinCode.Abstractions.Interfaces.AgentMetadata))]
public sealed partial class AgentMetadataJsonContext : JsonSerializerContext;