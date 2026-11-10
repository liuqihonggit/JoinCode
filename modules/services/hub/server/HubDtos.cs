
namespace Core.Hub.Server;

/// <summary>Agent 注册请求 DTO。</summary>
public sealed class AgentRegisterRequest {
    /// <summary>Agent 标识符。</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;

    /// <summary>会话标识符（可选）。</summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }
}

/// <summary>Agent 注销请求 DTO。</summary>
public sealed class AgentUnregisterRequest {
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
