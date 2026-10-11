
namespace McpClient;

/// <summary>
/// MCP 客户端选项 — 描述客户端名称、协议版本、超时与重试等可配置参数。
/// </summary>
public class McpClientOptions {
    /// <summary>客户端名称,发送至服务器作为 ClientInfo.Name。</summary>
    public string ClientName { get; init; } = "JoinCode.McpClient";

    /// <summary>客户端版本,发送至服务器作为 ClientInfo.Version。</summary>
    public string ClientVersion { get; init; } = "1.0.0";

    /// <summary>MCP 协议版本,用于握手协商。</summary>
    public string ProtocolVersion { get; init; } = McpProtocolVersion.Current;

    /// <summary>单次请求超时秒数。</summary>
    public int RequestTimeoutSeconds { get; init; } = 60;

    /// <summary>请求失败最大重试次数。</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>重试间隔毫秒数。</summary>
    public int RetryDelayMs { get; init; } = WorkflowConstants.Retry.DefaultRetryDelayMs;
}

/// <summary>
/// MCP 客户端选项构建器 — 提供链式 API 构造 McpClientOptions 实例。
/// </summary>
public sealed class McpClientOptionsBuilder {
    private string _clientName = "JoinCode.McpClient";
    private string _clientVersion = "1.0.0";
    private string _protocolVersion = McpProtocolVersion.Current;
    private int _requestTimeoutSeconds = 60;
    private int _maxRetries = 3;
    private int _retryDelayMs = WorkflowConstants.Retry.DefaultRetryDelayMs;

    private McpClientOptionsBuilder() { }

    /// <summary>创建构建器实例。</summary>
    /// <returns>新的 McpClientOptionsBuilder 实例。</returns>
    public static McpClientOptionsBuilder Create() => new();

    /// <summary>设置客户端名称。</summary>
    /// <param name="clientName">客户端名称。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpClientOptionsBuilder WithClientName(string clientName) { _clientName = clientName; return this; }

    /// <summary>设置客户端版本。</summary>
    /// <param name="clientVersion">客户端版本。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpClientOptionsBuilder WithClientVersion(string clientVersion) { _clientVersion = clientVersion; return this; }

    /// <summary>设置 MCP 协议版本。</summary>
    /// <param name="protocolVersion">协议版本字符串。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpClientOptionsBuilder WithProtocolVersion(string protocolVersion) { _protocolVersion = protocolVersion; return this; }

    /// <summary>设置单次请求超时秒数。</summary>
    /// <param name="seconds">超时秒数。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpClientOptionsBuilder WithRequestTimeout(int seconds) { _requestTimeoutSeconds = seconds; return this; }

    /// <summary>设置最大重试次数。</summary>
    /// <param name="maxRetries">最大重试次数。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpClientOptionsBuilder WithMaxRetries(int maxRetries) { _maxRetries = maxRetries; return this; }

    /// <summary>设置重试间隔毫秒数。</summary>
    /// <param name="delayMs">重试间隔毫秒数。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpClientOptionsBuilder WithRetryDelay(int delayMs) { _retryDelayMs = delayMs; return this; }

    /// <summary>禁用重试 — 将最大重试次数置为 0。</summary>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpClientOptionsBuilder DisableRetry() { _maxRetries = 0; return this; }

    /// <summary>构建 McpClientOptions 实例。</summary>
    /// <returns>填充完毕的 McpClientOptions 实例。</returns>
    public McpClientOptions Build() => new() {
        ClientName = _clientName,
        ClientVersion = _clientVersion,
        ProtocolVersion = _protocolVersion,
        RequestTimeoutSeconds = _requestTimeoutSeconds,
        MaxRetries = _maxRetries,
        RetryDelayMs = _retryDelayMs
    };
}

/// <summary>
/// MCP 服务器连接配置构建器 — 提供链式 API 构造 McpServerConnectionConfig 实例。
/// </summary>
public sealed class McpServerConnectionConfigBuilder {
    private string _name = string.Empty;
    private string _endpoint = string.Empty;
    private McpClientTransportType _transportType = McpClientTransportType.Stdio;
    private McpAuthConfig? _auth;
    private Dictionary<string, string> _environment = [];
    private string? _headersHelper;
    private Dictionary<string, string> _headers = [];

    private McpServerConnectionConfigBuilder() { }

    /// <summary>创建构建器实例。</summary>
    /// <returns>新的 McpServerConnectionConfigBuilder 实例。</returns>
    public static McpServerConnectionConfigBuilder Create() => new();

    /// <summary>设置服务器名称。</summary>
    /// <param name="name">服务器名称。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithName(string name) { _name = name; return this; }

    /// <summary>设置服务器端点 — Stdio 为命令行,HTTP/WebSocket 为 URL。</summary>
    /// <param name="endpoint">端点字符串。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithEndpoint(string endpoint) { _endpoint = endpoint; return this; }

    /// <summary>使用 Stdio 传输类型。</summary>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder UseStdio() { _transportType = McpClientTransportType.Stdio; return this; }

    /// <summary>使用 HTTP 传输类型。</summary>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder UseHttp() { _transportType = McpClientTransportType.Http; return this; }

    /// <summary>使用 WebSocket 传输类型。</summary>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder UseWebSocket() { _transportType = McpClientTransportType.WebSocket; return this; }

    /// <summary>显式设置传输类型。</summary>
    /// <param name="transportType">传输类型枚举值。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithTransportType(McpClientTransportType transportType) { _transportType = transportType; return this; }

    /// <summary>通过子构建器配置认证。</summary>
    /// <param name="configure">配置 McpAuthConfigBuilder 的委托。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithAuth(Action<McpAuthConfigBuilder> configure) {
        var builder = new McpAuthConfigBuilder();
        configure(builder);
        _auth = builder.Build();
        return this;
    }

    /// <summary>快捷设置 ApiKey 认证。</summary>
    /// <param name="apiKey">API Key 字符串。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithApiKey(string apiKey) { _auth = new McpAuthConfigBuilder().UseApiKey(apiKey).Build(); return this; }

    /// <summary>快捷设置 Bearer Token 认证。</summary>
    /// <param name="token">Bearer 令牌。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithBearerToken(string token) { _auth = new McpAuthConfigBuilder().UseBearer(token).Build(); return this; }

    /// <summary>快捷设置 Basic 认证。</summary>
    /// <param name="username">用户名。</param>
    /// <param name="password">密码。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithBasicAuth(string username, string password) { _auth = new McpAuthConfigBuilder().UseBasic(username, password).Build(); return this; }

    /// <summary>追加单个环境变量。</summary>
    /// <param name="key">变量名。</param>
    /// <param name="value">变量值。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithEnvironment(string key, string value) { _environment[key] = value; return this; }

    /// <summary>整体替换环境变量字典。</summary>
    /// <param name="environment">环境变量字典。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithEnvironment(Dictionary<string, string> environment) { _environment = environment; return this; }

    /// <summary>设置 Headers Helper 路径 — 用于外部鉴权脚本。</summary>
    /// <param name="headersHelper">Helper 路径或标识。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithHeadersHelper(string headersHelper) { _headersHelper = headersHelper; return this; }

    /// <summary>追加单个自定义请求头。</summary>
    /// <param name="key">头名称。</param>
    /// <param name="value">头值。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithHeader(string key, string value) { _headers[key] = value; return this; }

    /// <summary>整体替换自定义请求头字典。</summary>
    /// <param name="headers">请求头字典。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpServerConnectionConfigBuilder WithHeaders(Dictionary<string, string> headers) { _headers = headers; return this; }

    /// <summary>构建 McpServerConnectionConfig 实例。</summary>
    /// <returns>填充完毕的 McpServerConnectionConfig 实例。</returns>
    public McpServerConnectionConfig Build() => new() {
        Name = _name,
        Endpoint = _endpoint,
        TransportType = _transportType,
        Auth = _auth,
        Environment = _environment,
        HeadersHelper = _headersHelper,
        Headers = _headers
    };
}

/// <summary>
/// MCP 认证配置构建器 — 提供链式 API 构造 McpAuthConfig 实例,支持 None/ApiKey/Bearer/Basic/OAuth2 多种认证类型。
/// </summary>
public sealed class McpAuthConfigBuilder {
    private McpAuthType _type = McpAuthType.None;
    private string? _apiKey;
    private string? _bearerToken;
    private string? _username;
    private string? _password;
    private string? _clientId;
    private string? _clientSecret;
    private string? _tokenUrl;
    private List<string> _scopes = [];

    /// <summary>设置为无认证。</summary>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpAuthConfigBuilder UseNone() { _type = McpAuthType.None; return this; }

    /// <summary>设置为 ApiKey 认证。</summary>
    /// <param name="apiKey">API Key 字符串。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpAuthConfigBuilder UseApiKey(string apiKey) { _type = McpAuthType.ApiKey; _apiKey = apiKey; return this; }

    /// <summary>设置为 Bearer Token 认证。</summary>
    /// <param name="token">Bearer 令牌。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpAuthConfigBuilder UseBearer(string token) { _type = McpAuthType.Bearer; _bearerToken = token; return this; }

    /// <summary>设置为 Basic 认证。</summary>
    /// <param name="username">用户名。</param>
    /// <param name="password">密码。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpAuthConfigBuilder UseBasic(string username, string password) { _type = McpAuthType.Basic; _username = username; _password = password; return this; }

    /// <summary>设置为 OAuth2 认证。</summary>
    /// <param name="clientId">客户端 ID。</param>
    /// <param name="clientSecret">客户端密钥。</param>
    /// <param name="tokenUrl">令牌端点 URL。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpAuthConfigBuilder UseOAuth2(string clientId, string clientSecret, string tokenUrl) { _type = McpAuthType.OAuth2; _clientId = clientId; _clientSecret = clientSecret; _tokenUrl = tokenUrl; return this; }

    /// <summary>追加单个 OAuth2 scope。</summary>
    /// <param name="scope">scope 字符串。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpAuthConfigBuilder WithScope(string scope) { _scopes.Add(scope); return this; }

    /// <summary>整体替换 OAuth2 scope 列表。</summary>
    /// <param name="scopes">scope 数组。</param>
    /// <returns>当前构建器实例,用于链式调用。</returns>
    public McpAuthConfigBuilder WithScopes(params string[] scopes) { _scopes = scopes.ToList(); return this; }

    /// <summary>构建 McpAuthConfig 实例。</summary>
    /// <returns>填充完毕的 McpAuthConfig 实例。</returns>
    public McpAuthConfig Build() => new() {
        Type = _type,
        ApiKey = _apiKey,
        BearerToken = _bearerToken,
        Username = _username,
        Password = _password,
        ClientId = _clientId,
        ClientSecret = _clientSecret,
        TokenUrl = _tokenUrl,
        Scopes = _scopes
    };
}