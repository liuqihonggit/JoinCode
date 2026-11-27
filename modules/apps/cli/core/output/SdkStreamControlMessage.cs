namespace JoinCode.Cli.Output;

/// <summary>
/// SDK 流式输入的 control_request（TASK142-B）— 形状取自基准侧
/// <c>entrypoints/sdk/controlSchemas.ts::SDKControlRequestSchema</c>。
/// 本轮只消费 <c>interrupt</c>，其余子类型由解析器给诊断。
/// </summary>
public sealed record SdkStreamControlRequest {
    /// <summary>消息类型（control_request）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>请求 ID — 应答必须原样带回</summary>
    [System.Text.Json.Serialization.JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <summary>请求体</summary>
    [System.Text.Json.Serialization.JsonPropertyName("request")]
    public SdkStreamControlRequestBody? Request { get; init; }
}

/// <summary>控制请求体 — subtype 决定动作。</summary>
public sealed record SdkStreamControlRequestBody {
    /// <summary>请求子类型（interrupt / set_permission_mode / initialize / …）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("subtype")]
    public string Subtype { get; init; } = string.Empty;
}

/// <summary>
/// 发给 SDK 客户端的控制应答 — 基准侧 <c>sendControlResponseSuccess</c>：
/// <c>{"type":"control_response","response":{"subtype":"success","request_id":"…","response":{}}}</c>。
/// </summary>
public sealed record SdkStreamControlResponse {
    /// <summary>消息类型</summary>
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type => "control_response";

    /// <summary>应答体</summary>
    [System.Text.Json.Serialization.JsonPropertyName("response")]
    public required SdkStreamControlResponseBody Response { get; init; }
}

/// <summary>控制应答体。</summary>
public sealed record SdkStreamControlResponseBody {
    /// <summary>应答子类型（success/error）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("subtype")]
    public string Subtype { get; init; } = "success";

    /// <summary>原请求 ID</summary>
    [System.Text.Json.Serialization.JsonPropertyName("request_id")]
    public required string RequestId { get; init; }

    /// <summary>成功时的空响应对象（基准侧为 <c>{}</c>）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("response")]
    public Dictionary<string, string> Response { get; init; } = new();
}
