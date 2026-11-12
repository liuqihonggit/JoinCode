
namespace Core.Hub.Server;

/// <summary>Agent 注册请求 DTO。</summary>
public sealed class AgentRegisterRequest {
    /// <summary>Agent 标识符。</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;

    /// <summary>会话标识符（可选）。</summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    /// <summary>能力标签列表（可选，用于按能力路由）。</summary>
    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; init; } = [];
}

/// <summary>Agent 注销请求 DTO。</summary>
public sealed class AgentUnregisterRequest {
    /// <summary>Agent 标识符。</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;
}

/// <summary>Agent 心跳请求 DTO。</summary>
public sealed class AgentHeartbeatRequest {
    /// <summary>Agent 标识符。</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;
}

/// <summary>Agent 间消息转发请求 DTO。</summary>
public sealed class AgentMessageRequest {
    /// <summary>发送方 Agent 标识符。</summary>
    [JsonPropertyName("from_agent_id")]
    public string FromAgentId { get; init; } = string.Empty;

    /// <summary>接收方 Agent 标识符（null 表示广播）。</summary>
    [JsonPropertyName("to_agent_id")]
    public string? ToAgentId { get; init; }

    /// <summary>消息类型（如 "text"）。</summary>
    [JsonPropertyName("message_type")]
    public string MessageType { get; init; } = "text";

    /// <summary>消息内容。</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>消息时间戳。</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>按能力路由消息请求 DTO。</summary>
public sealed class AgentRouteRequest {
    /// <summary>目标能力标签。</summary>
    [JsonPropertyName("capability")]
    public string Capability { get; init; } = string.Empty;

    /// <summary>发送方 Agent 标识符。</summary>
    [JsonPropertyName("from_agent_id")]
    public string FromAgentId { get; init; } = string.Empty;

    /// <summary>消息类型（如 "text"）。</summary>
    [JsonPropertyName("message_type")]
    public string MessageType { get; init; } = "text";

    /// <summary>消息内容。</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

/// <summary>按能力路由消息响应 DTO。</summary>
public sealed class HubRouteResponse {
    /// <summary>操作状态（"delivered" 或 "no_agent"）。</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>被选中的目标 Agent 标识符（成功时）。</summary>
    [JsonPropertyName("agent_id")]
    public string? AgentId { get; init; }
}

/// <summary>健康检查响应 DTO。</summary>
public sealed class HubHealthResponse {
    /// <summary>服务器状态（"ok"）。</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>已连接 Agent 数量。</summary>
    [JsonPropertyName("agents")]
    public int Agents { get; init; }

    /// <summary>已注册 Provider 数量。</summary>
    [JsonPropertyName("providers")]
    public int Providers { get; init; }
}

/// <summary>Agent 列表响应 DTO。</summary>
public sealed class HubAgentsResponse {
    /// <summary>已注册的 Agent 列表。</summary>
    [JsonPropertyName("agents")]
    public List<AgentInfo> Agents { get; init; } = [];
}

/// <summary>错误响应 DTO。</summary>
public sealed class HubErrorResponse {
    /// <summary>错误描述。</summary>
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}

/// <summary>状态响应 DTO。</summary>
public sealed class HubStatusResponse {
    /// <summary>操作状态（如 "ok"、"delivered"、"broadcast"）。</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}

/// <summary>Agent 注册响应 DTO。</summary>
public sealed class HubRegisterResponse {
    /// <summary>操作状态（"ok"）。</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>Agent 标识符。</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;
}

/// <summary>模型信息 DTO。</summary>
public sealed class HubModelInfo {
    /// <summary>模型名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>所属 Provider。</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;

    /// <summary>拥有者。</summary>
    [JsonPropertyName("owned_by")]
    public string OwnedBy { get; init; } = string.Empty;

    /// <summary>上下文窗口大小。</summary>
    [JsonPropertyName("context_window")]
    public int ContextWindow { get; init; }
}

/// <summary>模型列表响应 DTO（兼容 OpenAI /v1/models 格式）。</summary>
public sealed class HubModelsResponse {
    /// <summary>模型列表。</summary>
    [JsonPropertyName("data")]
    public List<HubModelInfo> Data { get; init; } = [];
}
