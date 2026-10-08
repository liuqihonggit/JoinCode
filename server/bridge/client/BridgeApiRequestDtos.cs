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
