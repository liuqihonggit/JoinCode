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

    /// <summary>
    /// <c>set_permission_mode</c> 的目标模式（TASK143，基准侧
    /// <c>SDKControlSetPermissionModeRequestSchema.request.mode</c>）。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("mode")]
    public string? Mode { get; init; }
}

/// <summary>
/// 发给 SDK 客户端的控制应答 — 基准侧 <c>sendControlResponseSuccess</c>：
/// <c>{"type":"control_response","response":{"subtype":"success","request_id":"…","response":{}}}</c>；
/// 失败走 error 分支（<c>{subtype:"error", request_id, error}</c>，无 response 对象）。
/// </summary>
public sealed record SdkStreamControlResponse {
    /// <summary>消息类型</summary>
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type => "control_response";

    /// <summary>应答体</summary>
    [System.Text.Json.Serialization.JsonPropertyName("response")]
    public required SdkStreamControlResponseBody Response { get; init; }

    /// <summary>
    /// 成功应答（空载荷，对齐基准 <c>response:{}</c>）— 用于 interrupt 这类没有返回值的指令。
    /// </summary>
    /// <param name="requestId">原请求 ID</param>
    /// <returns>应答消息</returns>
    public static SdkStreamControlResponse Success(string requestId) => new() {
        Response = new SdkStreamControlResponseBody {
            Subtype = "success",
            RequestId = requestId,
            Response = new Dictionary<string, string>(),
        },
    };

    /// <summary>
    /// 成功应答 — 带回**实际生效**的权限模式。
    /// <para>为什么不返回空对象：bypass 受 settings.json 的 <c>disableBypassPermissionsMode</c> 安全闸管控，
    /// 被拦下时实际模式与请求值不同（bridge_serve 的 <c>permission_mode_changed</c> 同此语义）。
    /// 基准侧 <c>response</c> 是开放 record（<c>z.record</c>），带 <c>mode</c> 键合法。</para>
    /// </summary>
    /// <param name="requestId">原请求 ID</param>
    /// <param name="effectiveMode">实际生效模式</param>
    /// <returns>应答消息</returns>
    public static SdkStreamControlResponse Success(string requestId, string effectiveMode) => new() {
        Response = new SdkStreamControlResponseBody {
            Subtype = "success",
            RequestId = requestId,
            Response = new Dictionary<string, string> { ["mode"] = effectiveMode },
        },
    };

    /// <summary>失败应答 — 基准侧 error 分支（带可读原因，客户端据此修正请求）。</summary>
    /// <param name="requestId">原请求 ID</param>
    /// <param name="error">失败原因</param>
    /// <returns>应答消息</returns>
    public static SdkStreamControlResponse Failure(string requestId, string error) => new() {
        Response = new SdkStreamControlResponseBody {
            Subtype = "error",
            RequestId = requestId,
            Error = error,
        },
    };
}

/// <summary>控制应答体。</summary>
public sealed record SdkStreamControlResponseBody {
    /// <summary>应答子类型（success/error）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("subtype")]
    public string Subtype { get; init; } = "success";

    /// <summary>原请求 ID</summary>
    [System.Text.Json.Serialization.JsonPropertyName("request_id")]
    public required string RequestId { get; init; }

    /// <summary>成功时的响应载荷（基准侧为 <c>{}</c>；本仓成功时带实际生效模式）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("response")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Response { get; init; }

    /// <summary>失败原因（仅 error 分支写；基准侧 <c>ControlErrorResponseSchema.error</c>）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}
