namespace McpToolDispatch;

/// <summary>
/// MCP 认证配置持久化 DTO — 序列化到 ~/.jcc/mcp/auth.json。
/// 包含敏感信息（API Key/Token/Password），因为 ~/.jcc/ 是用户私有目录。
/// 支持 CLI 无状态模式跨进程共享认证配置。
/// </summary>
public sealed class McpAuthStateData
{
    public List<McpAuthEntry> AuthConfigs { get; set; } = new();
}

/// <summary>
/// 单条认证配置持久化记录。
/// </summary>
public sealed class McpAuthEntry
{
    public string AuthName { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty;

    public string? ApiKey { get; set; }
    public string? HeaderName { get; set; }

    public string? Token { get; set; }

    public string? Username { get; set; }
    public string? Password { get; set; }

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? TokenUrl { get; set; }
    public List<string> Scopes { get; set; } = new();
}
