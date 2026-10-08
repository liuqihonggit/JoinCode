namespace Core.Bridge;

// Bridge API 请求体 DTO — 用于替代手写 JSON 拼接 + EscapeJsonString
// [JsonPropertyName] 明确指定 snake_case 字段名，不受 BridgeJsonContext CamelCase 策略影响

/// <summary>Bridge 更新标题请求体 — PATCH /v1/sessions/{id}</summary>
internal sealed class BridgeUpdateTitleRequest {
    /// <summary>会话标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
}

/// <summary>Bridge 重连请求体 — POST /v1/environments/{env}/bridge/reconnect</summary>
internal sealed class BridgeReconnectRequestBody {
    /// <summary>会话 ID</summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = "";
}

/// <summary>Bridge 重连会话请求体 — POST /v1/environments/bridge/{env}/sessions/{id}/bridge/reconnect</summary>
internal sealed class BridgeReconnectSessionRequestBody {
    /// <summary>环境 ID</summary>
    [JsonPropertyName("environment_id")]
    public string EnvironmentId { get; init; } = "";
    /// <summary>会话 ID</summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = "";
}

/// <summary>Bridge 创建会话请求体 — POST /v1/code/sessions（JSON 序列化专用，与 BridgeMainTypes.BridgeCreateSessionRequest 不同）</summary>
internal sealed class BridgeCreateSessionRequestBody {
    /// <summary>环境 ID</summary>
    [JsonPropertyName("environment_id")]
    public string EnvironmentId { get; init; } = "";
    /// <summary>来源(固定 remote-control)</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = "remote-control";
    /// <summary>标题(可选)</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }
    /// <summary>事件 JSON 数组字符串</summary>
    [JsonPropertyName("events")]
    public string Events { get; init; } = "[]";
    /// <summary>会话上下文</summary>
    [JsonPropertyName("session_context")]
    public BridgeSessionContextRequestBody? SessionContext { get; init; }
    /// <summary>权限模式(可选)</summary>
    [JsonPropertyName("permission_mode")]
    public string? PermissionMode { get; init; }
}

/// <summary>Bridge 会话上下文请求体</summary>
internal sealed class BridgeSessionContextRequestBody {
    /// <summary>来源列表(git repository 等)</summary>
    [JsonPropertyName("sources")]
    public List<BridgeGitSourceRequest> Sources { get; init; } = new();
    /// <summary>结果列表(固定空)</summary>
    [JsonPropertyName("outcomes")]
    public List<object> Outcomes { get; init; } = new();
}

/// <summary>Bridge git source 请求体 — session_context.sources 数组项</summary>
internal sealed class BridgeGitSourceRequest {
    /// <summary>类型(固定 git_repository)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "git_repository";
    /// <summary>仓库 URL</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = "";
    /// <summary>分支/修订版本(可选)</summary>
    [JsonPropertyName("revision")]
    public string? Revision { get; init; }
}

/// <summary>Bridge 设备注册请求体 — POST /api/auth/trusted_devices</summary>
internal sealed class BridgeEnrollDeviceRequest {
    /// <summary>设备显示名称</summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "";
}

/// <summary>Bridge device_token 提取响应 — 从 auth 文件或 API 响应提取 device_token</summary>
internal sealed class BridgeDeviceTokenResponse {
    /// <summary>设备令牌</summary>
    [JsonPropertyName("device_token")]
    public string? DeviceToken { get; init; }
}

/// <summary>Bridge 创建代码会话请求体 — POST /v1/code/sessions</summary>
internal sealed class BridgeCreateCodeSessionRequest {
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>bridge 占位对象(空对象)</summary>
    [JsonPropertyName("bridge")]
    public object? Bridge { get; init; } = new();
    /// <summary>标签列表(可选,null 时 WhenWritingNull 忽略)</summary>
#pragma warning disable JCC11002
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; init; }
#pragma warning restore JCC11002
}

/// <summary>Bridge 代码会话响应 — 提取 session.id</summary>
internal sealed class BridgeCodeSessionResponse {
    /// <summary>session 节点</summary>
    [JsonPropertyName("session")]
    public BridgeCodeSessionIdResponse? Session { get; init; }
}

/// <summary>Bridge 代码会话 ID 响应</summary>
internal sealed class BridgeCodeSessionIdResponse {
    /// <summary>会话 ID(cse_ 前缀)</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>Bridge 结果消息 — MakeResultMessage 的 JSON 结构</summary>
internal sealed class BridgeResultMessageDto {
    /// <summary>类型(固定 result)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "result";
    /// <summary>子类型(固定 success)</summary>
    [JsonPropertyName("subtype")]
    public string Subtype { get; init; } = "success";
    /// <summary>持续时间(毫秒)</summary>
    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; init; }
    /// <summary>API 持续时间(毫秒)</summary>
    [JsonPropertyName("duration_api_ms")]
    public int DurationApiMs { get; init; }
    /// <summary>是否错误</summary>
    [JsonPropertyName("is_error")]
    public bool IsError { get; init; }
    /// <summary>轮次数</summary>
    [JsonPropertyName("num_turns")]
    public int NumTurns { get; init; }
    /// <summary>结果文本</summary>
    [JsonPropertyName("result")]
    public string Result { get; init; } = "";
    /// <summary>停止原因(null)</summary>
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }
    /// <summary>总成本(USD)</summary>
    [JsonPropertyName("total_cost_usd")]
    public decimal TotalCostUsd { get; init; }
    /// <summary>用量(空对象)</summary>
    [JsonPropertyName("usage")]
    public object Usage { get; init; } = new();
    /// <summary>模型用量(空对象)</summary>
    [JsonPropertyName("modelUsage")]
    public object ModelUsage { get; init; } = new();
    /// <summary>权限拒绝列表(空数组)</summary>
    [JsonPropertyName("permission_denials")]
    public List<object> PermissionDenials { get; init; } = new();
    /// <summary>会话 ID</summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = "";
    /// <summary>UUID</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = "";
}

/// <summary>Bridge control_response 消息</summary>
internal sealed class BridgeControlResponseDto {
    /// <summary>类型(固定 control_response)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "control_response";
    /// <summary>请求 ID</summary>
    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = "";
    /// <summary>会话 ID</summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = "";
    /// <summary>响应体</summary>
    [JsonPropertyName("response")]
    public BridgeControlResponseBodyDto Response { get; init; } = new();
}

/// <summary>Bridge keep_alive 消息</summary>
internal sealed class BridgeKeepAliveMessageDto {
    /// <summary>类型(固定 keep_alive)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "keep_alive";
    /// <summary>会话 ID</summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }
}

/// <summary>Bridge cancel_control_request 消息</summary>
internal sealed class BridgeCancelControlRequestMessageDto {
    /// <summary>类型(固定 cancel_control_request)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "cancel_control_request";
    /// <summary>请求 ID</summary>
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }
}

/// <summary>Bridge 简化 result 消息(V1 协议)</summary>
internal sealed class BridgeSimpleResultMessageDto {
    /// <summary>类型(固定 result)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "result";
    /// <summary>会话 ID</summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }
}

/// <summary>Bridge update_environment_variables 消息</summary>
internal sealed class BridgeUpdateEnvVarsMessageDto {
    /// <summary>类型(固定 update_environment_variables)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "update_environment_variables";
    /// <summary>环境变量字典</summary>
    [JsonPropertyName("variables")]
    public required Dictionary<string, string> Variables { get; init; }
}

/// <summary>Bridge V2 control_cancel_request 消息</summary>
internal sealed class BridgeV2ControlCancelRequestDto {
    /// <summary>类型(固定 control_cancel_request)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "control_cancel_request";
    /// <summary>请求 ID</summary>
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }
    /// <summary>会话 ID</summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }
}

/// <summary>Bridge 权限请求消息</summary>
internal sealed class BridgePermissionRequestDto {
    /// <summary>类型(固定 control_request)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "control_request";
    /// <summary>请求 ID</summary>
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }
    /// <summary>请求体</summary>
    [JsonPropertyName("request")]
    public required BridgePermissionRequestBodyDto Request { get; init; }
}

/// <summary>Bridge 权限请求体</summary>
internal sealed class BridgePermissionRequestBodyDto {
    /// <summary>子类型(固定 permission_request)</summary>
    [JsonPropertyName("subtype")]
    public string Subtype { get; init; } = "permission_request";
    /// <summary>工具名称</summary>
    [JsonPropertyName("tool_name")]
    public required string ToolName { get; init; }
    /// <summary>工具使用标识</summary>
    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }
    /// <summary>权限请求描述</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }
    /// <summary>权限建议列表(可选)</summary>
#pragma warning disable JCC11002
    [JsonPropertyName("permission_suggestions")]
    public List<BridgePermissionSuggestionDto>? PermissionSuggestions { get; init; }
#pragma warning restore JCC11002
    /// <summary>被阻止的路径(可选)</summary>
    [JsonPropertyName("blocked_path")]
    public string? BlockedPath { get; init; }
}

/// <summary>Bridge 权限建议</summary>
internal sealed class BridgePermissionSuggestionDto {
    /// <summary>工具名称</summary>
    [JsonPropertyName("tool_name")]
    public required string ToolName { get; init; }
    /// <summary>权限模式</summary>
    [JsonPropertyName("permission_mode")]
    public required string PermissionMode { get; init; }
}

/// <summary>Bridge 权限响应消息</summary>
internal sealed class BridgePermissionResponseMessageDto {
    /// <summary>类型(固定 control_response)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "control_response";
    /// <summary>请求 ID</summary>
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }
    /// <summary>响应体</summary>
    [JsonPropertyName("response")]
    public required BridgePermissionResponseBodyDto Response { get; init; }
}

/// <summary>Bridge 权限响应体</summary>
internal sealed class BridgePermissionResponseBodyDto {
    /// <summary>权限行为</summary>
    [JsonPropertyName("behavior")]
    public required string Behavior { get; init; }
    /// <summary>消息(可选)</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>Bridge 权限取消请求</summary>
internal sealed class BridgePermissionCancelDto {
    /// <summary>类型(固定 control_request)</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "control_request";
    /// <summary>请求 ID</summary>
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }
    /// <summary>请求体</summary>
    [JsonPropertyName("request")]
    public required BridgePermissionCancelBodyDto Request { get; init; }
}

/// <summary>Bridge 权限取消请求体</summary>
internal sealed class BridgePermissionCancelBodyDto {
    /// <summary>子类型(固定 permission_cancel)</summary>
    [JsonPropertyName("subtype")]
    public string Subtype { get; init; } = "permission_cancel";
}

/// <summary>Bridge control_response 响应体</summary>
internal sealed class BridgeControlResponseBodyDto {
    /// <summary>是否成功</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }
    /// <summary>错误信息(可选)</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
