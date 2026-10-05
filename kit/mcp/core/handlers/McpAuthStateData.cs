namespace McpToolDispatch;

/// <summary>
/// MCP 认证配置持久化 DTO — 序列化到 ~/.jcc/mcp/auth.json。
/// 包含敏感信息（API Key/Token/Password），因为 ~/.jcc/ 是用户私有目录。
/// 支持 CLI 无状态模式跨进程共享认证配置。
/// </summary>
public sealed record McpAuthStateData {
    /// <summary>认证配置列表</summary>
    public List<McpAuthEntry> AuthConfigs { get; init; } = new();
}

/// <summary>
/// 单条认证配置持久化记录。
/// </summary>
public sealed record McpAuthEntry {
    /// <summary>认证配置名称（用于后续引用）</summary>
    public string AuthName { get; init; } = string.Empty;
    /// <summary>认证类型（ApiKey/Bearer/Basic/OAuth2）</summary>
    public string AuthType { get; init; } = string.Empty;

    /// <summary>API 密钥（ApiKey 类型使用）</summary>
    public string? ApiKey { get; init; }
    /// <summary>请求头名称（ApiKey 类型使用，默认 X-API-Key）</summary>
    public string? HeaderName { get; init; }

    /// <summary>Bearer Token（Bearer 类型使用）</summary>
    public string? Token { get; init; }

    /// <summary>用户名（Basic 类型使用）</summary>
    public string? Username { get; init; }
    /// <summary>密码（Basic 类型使用）</summary>
    public string? Password { get; init; }

    /// <summary>OAuth2 客户端 ID</summary>
    public string? ClientId { get; init; }
    /// <summary>OAuth2 客户端密钥</summary>
    public string? ClientSecret { get; init; }
    /// <summary>OAuth2 令牌获取 URL</summary>
    public string? TokenUrl { get; init; }
    /// <summary>OAuth2 授权作用域列表</summary>
    public List<string> Scopes { get; init; } = new();
}