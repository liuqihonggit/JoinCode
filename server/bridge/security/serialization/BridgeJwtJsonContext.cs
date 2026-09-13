namespace Core.Bridge;

/// <summary>
/// Bridge JWT JSON 序列化上下文 — 为 BridgeJwtPayload 提供 AOT 兼容的源码生成序列化器
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BridgeJwtPayload))]
internal partial class BridgeJwtJsonContext : JsonSerializerContext;
