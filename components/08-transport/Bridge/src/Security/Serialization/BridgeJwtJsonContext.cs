namespace Core.Bridge;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false)]
[JsonSerializable(typeof(BridgeJwtPayload))]
internal partial class BridgeJwtJsonContext : JsonSerializerContext;
