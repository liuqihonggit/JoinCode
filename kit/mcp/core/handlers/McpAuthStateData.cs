namespace McpToolDispatch;

/// <summary>
/// MCP 认证配置持久化 DTO — 序列化到 ~/.jcc/mcp/auth.json。
/// 包含敏感信息（API Key/Token/Password），因为 ~/.jcc/ 是用户私有目录。
/// 支持 CLI 无状态模式跨进程共享认证配置。
/// </summary>
public sealed class McpAuthStateData
{
    /// <summary>认证配置列表</summary>
    public List<McpAuthEntry> AuthConfigs { get; set; } = new();
}

/// <summary>
/// 单条认证配置持久化记录。
/// </summary>
public sealed class McpAuthEntry
{
    /// <summary>认证配置名称（用于后续引用）</summary>
    public string AuthName { get; set; } = string.Empty;
    /// <summary>认证类型（ApiKey/Bearer/Basic/OAuth2）</summary>
    public string AuthType { get; set; } = string.Empty;

    /// <summary>API 密钥（ApiKey 类型使用）</summary>
    public string? ApiKey { get; set; }
    /// <summary>请求头名称（ApiKey 类型使用，默认 X-API-Key）</summary>
    public string? HeaderName { get; set; }

    /// <summary>Bearer Token（Bearer 类型使用）</summary>
    public string? Token { get; set; }

    /// <summary>用户名（Basic 类型使用）</summary>
    public string? Username { get; set; }
    /// <summary>密码（Basic 类型使用）</summary>
    public string? Password { get; set; }

    /// <summary>OAuth2 客户端 ID</summary>
    public string? ClientId { get; set; }
    /// <summary>OAuth2 客户端密钥</summary>
    public string? ClientSecret { get; set; }
    /// <summary>OAuth2 令牌获取 URL</summary>
    public string? TokenUrl { get; set; }
    /// <summary>OAuth2 授权作用域列表</summary>
    public List<string> Scopes { get; set; } = new();
}
