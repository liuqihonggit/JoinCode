namespace JoinCode.Transport.Bridge;

/// <summary>
/// Transport.Impl 内部 JSON 序列化上下文 — AOT 兼容
/// 仅注册 Transport 层需要的类型
/// </summary>
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(BridgeReportStatePayload))]
[JsonSerializable(typeof(BridgeReportDeliveryPayload))]
[JsonSerializable(typeof(BridgeRegisterWorkerPayload))]
[JsonSerializable(typeof(BridgeErrorDetailDto))]
[JsonSerializable(typeof(BridgeErrorNestedDto))]
[JsonSerializable(typeof(JwtPayloadExpDto))]
[JsonSerializable(typeof(SseEventDataDto))]
[JsonSerializable(typeof(NdjsonControlRequestDto))]
[JsonSerializable(typeof(NdjsonAssistantMessageDto))]
[JsonSerializable(typeof(NdjsonToolUseBlockDto))]
[JsonSerializable(typeof(NdjsonTextBlockDto))]
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

/// <summary>Bridge 错误详情响应体 — 提取 message 或 error.message</summary>
public sealed class BridgeErrorDetailDto {
    /// <summary>顶层错误消息</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>嵌套错误对象</summary>
    [JsonPropertyName("error")]
    public BridgeErrorNestedDto? Error { get; set; }
}

/// <summary>Bridge 嵌套错误对象 — 提取 error.message</summary>
public sealed class BridgeErrorNestedDto {
    /// <summary>嵌套错误消息</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>JWT payload exp 声明提取 DTO — 仅提取 exp 字段</summary>
public sealed class JwtPayloadExpDto {
    /// <summary>JWT 过期时间（unix 秒）</summary>
    [JsonPropertyName("exp")]
    public long? Exp { get; set; }
}

/// <summary>SSE 事件数据 DTO — 提取 event_id</summary>
public sealed class SseEventDataDto {
    /// <summary>事件 ID</summary>
    [JsonPropertyName("event_id")]
    public string? EventId { get; set; }
}