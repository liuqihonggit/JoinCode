namespace JoinCode.Transport.Bridge;

/// <summary>
/// Transport.Impl 内部 JSON 序列化上下文 — AOT 兼容
/// 仅注册 Transport 层需要的类型
/// </summary>
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(BridgeReportStatePayload))]
[JsonSerializable(typeof(BridgeReportDeliveryPayload))]
[JsonSerializable(typeof(BridgeRegisterWorkerPayload))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class TransportBridgeJsonContext : JsonSerializerContext;

/// <summary>Worker 状态上报请求体</summary>
public sealed class BridgeReportStatePayload {
    /// <summary>会话活动状态</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }
}

/// <summary>事件投递状态上报请求体</summary>
public sealed class BridgeReportDeliveryPayload {
    /// <summary>事件 ID</summary>
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    /// <summary>投递状态</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

/// <summary>Worker 注册请求体</summary>
public sealed class BridgeRegisterWorkerPayload {
    /// <summary>会话 ID</summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }
}