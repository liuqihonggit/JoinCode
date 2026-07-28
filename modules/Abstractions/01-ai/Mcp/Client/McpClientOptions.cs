namespace JoinCode.Abstractions.Mcp.Client;

public class McpServerConnectionConfig
{
    public string Name { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public McpClientTransportType TransportType { get; init; } = McpClientTransportType.Stdio;
    public McpAuthConfig? Auth { get; init; }
    public Dictionary<string, string>? Environment { get; init; }
    public string? HeadersHelper { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
}

public enum McpClientTransportType
{
    [EnumValue("stdio")] Stdio,
    [EnumValue("sse")] Sse,
    [EnumValue("http")] Http,
    [EnumValue("websocket")] WebSocket,
}

public class McpAuthConfig
{
    public McpAuthType Type { get; init; } = McpAuthType.None;
    public string? ApiKey { get; init; }
    public string? BearerToken { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? TokenUrl { get; init; }
    public List<string>? Scopes { get; init; }
}

public enum McpAuthType
{
    [EnumValue("none")] None,
    [EnumValue("apikey")] ApiKey,
    [EnumValue("bearer")] Bearer,
    [EnumValue("basic")] Basic,
    [EnumValue("oauth2")] OAuth2,
}
