namespace McpToolDispatch;

/// <summary>
/// MCP 连接持久化状态 DTO — 序列化到 ~/.jcc/mcp/connections.json
/// 用于 CLI 无状态模式（mcp_call 单次调用）下跨进程共享 MCP 连接配置
/// 注意：只持久化连接配置（endpoint/transport/auth_name），不持久化敏感凭证（token/password）
/// 启动时根据配置重新建立连接
/// </summary>
public sealed record McpConnectionStateData {
    /// <summary>MCP 连接配置列表</summary>
    public List<McpConnectionEntry> Connections { get; init; } = [];
}

/// <summary>
/// 单个 MCP 连接的持久化条目
/// </summary>
public sealed record McpConnectionEntry {
    /// <summary>连接名称（用于后续引用）</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>服务器端点（命令或 URL）</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>传输类型: stdio, http, websocket</summary>
    public string TransportType { get; init; } = "stdio";

    /// <summary>是否使用 OAuth 认证</summary>
    public bool UseOAuth { get; init; }

    /// <summary>认证配置名称（引用 mcp_auth_* 配置，不内联敏感凭证）</summary>
    public string? AuthName { get; init; }
}