namespace JoinCode.Abstractions.Mcp.Client;

public class McpServerConnectionConfig {
    /// <summary>获取服务器名称。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>获取服务器端点。</summary>
    public string Endpoint { get; init; } = string.Empty;
    /// <summary>获取传输类型。</summary>
    public McpClientTransportType TransportType { get; init; } = McpClientTransportType.Stdio;
    /// <summary>获取认证配置。</summary>
    public McpAuthConfig? Auth { get; init; }
    /// <summary>获取环境变量字典。</summary>
    public Dictionary<string, string> Environment { get; init; } = [];
    /// <summary>获取请求头辅助脚本路径。</summary>
    public string? HeadersHelper { get; init; }
    /// <summary>获取请求头字典。</summary>
    public Dictionary<string, string> Headers { get; init; } = [];
}

public enum McpClientTransportType {
    [EnumValue("stdio")] Stdio,
    [EnumValue("http")] Http,
    [EnumValue("websocket")] WebSocket,
}

public class McpAuthConfig {
    /// <summary>获取认证类型。</summary>
    public McpAuthType Type { get; init; } = McpAuthType.None;
    /// <summary>获取 API Key。</summary>
    public string? ApiKey { get; init; }
    /// <summary>获取 Bearer Token。</summary>
    public string? BearerToken { get; init; }
    /// <summary>获取用户名。</summary>
    public string? Username { get; init; }
    /// <summary>获取密码。</summary>
    public string? Password { get; init; }
    /// <summary>获取 OAuth2 客户端标识。</summary>
    public string? ClientId { get; init; }
    /// <summary>获取 OAuth2 客户端密钥。</summary>
    public string? ClientSecret { get; init; }
    /// <summary>获取 OAuth2 Token 端点 URL。</summary>
    public string? TokenUrl { get; init; }
    /// <summary>获取 OAuth2 授权范围列表。</summary>
    public List<string> Scopes { get; init; } = [];
}

public enum McpAuthType {
    [EnumValue("none")] None,
    [EnumValue("apikey")] ApiKey,
    [EnumValue("bearer")] Bearer,
    [EnumValue("basic")] Basic,
    [EnumValue("oauth2")] OAuth2,
}