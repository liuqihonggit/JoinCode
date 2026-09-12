namespace JoinCode.Transport.Bridge;

/// <summary>
/// Transport.Impl 内部 JSON 序列化上下文 — AOT 兼容
/// 仅注册 Transport 层需要的类型
/// </summary>
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class TransportBridgeJsonContext : JsonSerializerContext;
