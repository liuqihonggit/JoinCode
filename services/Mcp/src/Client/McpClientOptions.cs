
namespace McpClient;

public class McpClientOptions
{
    public string ClientName { get; init; } = "JoinCode.McpClient";
    public string ClientVersion { get; init; } = "1.0.0";
    public string ProtocolVersion { get; init; } = "2024-11-05";
    public int RequestTimeoutSeconds { get; init; } = 60;
    public int MaxRetries { get; init; } = 3;
    public int RetryDelayMs { get; init; } = WorkflowConstants.Retry.DefaultRetryDelayMs;
}

public sealed class McpClientOptionsBuilder
{
    private string _clientName = "JoinCode.McpClient";
    private string _clientVersion = "1.0.0";
    private string _protocolVersion = "2024-11-05";
    private int _requestTimeoutSeconds = 60;
    private int _maxRetries = 3;
    private int _retryDelayMs = WorkflowConstants.Retry.DefaultRetryDelayMs;

    private McpClientOptionsBuilder() { }

    public static McpClientOptionsBuilder Create() => new();

    public McpClientOptionsBuilder WithClientName(string clientName) { _clientName = clientName; return this; }
    public McpClientOptionsBuilder WithClientVersion(string clientVersion) { _clientVersion = clientVersion; return this; }
    public McpClientOptionsBuilder WithProtocolVersion(string protocolVersion) { _protocolVersion = protocolVersion; return this; }
    public McpClientOptionsBuilder WithRequestTimeout(int seconds) { _requestTimeoutSeconds = seconds; return this; }
    public McpClientOptionsBuilder WithMaxRetries(int maxRetries) { _maxRetries = maxRetries; return this; }
    public McpClientOptionsBuilder WithRetryDelay(int delayMs) { _retryDelayMs = delayMs; return this; }
    public McpClientOptionsBuilder DisableRetry() { _maxRetries = 0; return this; }

    public McpClientOptions Build() => new()
    {
        ClientName = _clientName,
        ClientVersion = _clientVersion,
        ProtocolVersion = _protocolVersion,
        RequestTimeoutSeconds = _requestTimeoutSeconds,
        MaxRetries = _maxRetries,
        RetryDelayMs = _retryDelayMs
    };
}

public sealed class McpServerConnectionConfigBuilder
{
    private string _name = string.Empty;
    private string _endpoint = string.Empty;
    private McpClientTransportType _transportType = McpClientTransportType.Stdio;
    private McpAuthConfig? _auth;
    private Dictionary<string, string>? _environment;
    private string? _headersHelper;
    private Dictionary<string, string>? _headers;

    private McpServerConnectionConfigBuilder() { }

    public static McpServerConnectionConfigBuilder Create() => new();
    public McpServerConnectionConfigBuilder WithName(string name) { _name = name; return this; }
    public McpServerConnectionConfigBuilder WithEndpoint(string endpoint) { _endpoint = endpoint; return this; }
    public McpServerConnectionConfigBuilder UseStdio() { _transportType = McpClientTransportType.Stdio; return this; }
    public McpServerConnectionConfigBuilder UseSse() { _transportType = McpClientTransportType.Sse; return this; }
    public McpServerConnectionConfigBuilder UseHttp() { _transportType = McpClientTransportType.Http; return this; }
    public McpServerConnectionConfigBuilder UseWebSocket() { _transportType = McpClientTransportType.WebSocket; return this; }
    public McpServerConnectionConfigBuilder WithTransportType(McpClientTransportType transportType) { _transportType = transportType; return this; }

    public McpServerConnectionConfigBuilder WithAuth(Action<McpAuthConfigBuilder> configure)
    {
        var builder = new McpAuthConfigBuilder();
        configure(builder);
        _auth = builder.Build();
        return this;
    }

    public McpServerConnectionConfigBuilder WithApiKey(string apiKey) { _auth = new McpAuthConfigBuilder().UseApiKey(apiKey).Build(); return this; }
    public McpServerConnectionConfigBuilder WithBearerToken(string token) { _auth = new McpAuthConfigBuilder().UseBearer(token).Build(); return this; }
    public McpServerConnectionConfigBuilder WithBasicAuth(string username, string password) { _auth = new McpAuthConfigBuilder().UseBasic(username, password).Build(); return this; }

    public McpServerConnectionConfigBuilder WithEnvironment(string key, string value) { _environment ??= new Dictionary<string, string>(); _environment[key] = value; return this; }
    public McpServerConnectionConfigBuilder WithEnvironment(Dictionary<string, string> environment) { _environment = environment; return this; }
    public McpServerConnectionConfigBuilder WithHeadersHelper(string headersHelper) { _headersHelper = headersHelper; return this; }
    public McpServerConnectionConfigBuilder WithHeader(string key, string value) { _headers ??= new Dictionary<string, string>(); _headers[key] = value; return this; }
    public McpServerConnectionConfigBuilder WithHeaders(Dictionary<string, string> headers) { _headers = headers; return this; }

    public McpServerConnectionConfig Build() => new()
    {
        Name = _name,
        Endpoint = _endpoint,
        TransportType = _transportType,
        Auth = _auth,
        Environment = _environment,
        HeadersHelper = _headersHelper,
        Headers = _headers
    };
}

public sealed class McpAuthConfigBuilder
{
    private McpAuthType _type = McpAuthType.None;
    private string? _apiKey;
    private string? _bearerToken;
    private string? _username;
    private string? _password;
    private string? _clientId;
    private string? _clientSecret;
    private string? _tokenUrl;
    private List<string>? _scopes;

    public McpAuthConfigBuilder UseNone() { _type = McpAuthType.None; return this; }
    public McpAuthConfigBuilder UseApiKey(string apiKey) { _type = McpAuthType.ApiKey; _apiKey = apiKey; return this; }
    public McpAuthConfigBuilder UseBearer(string token) { _type = McpAuthType.Bearer; _bearerToken = token; return this; }
    public McpAuthConfigBuilder UseBasic(string username, string password) { _type = McpAuthType.Basic; _username = username; _password = password; return this; }
    public McpAuthConfigBuilder UseOAuth2(string clientId, string clientSecret, string tokenUrl) { _type = McpAuthType.OAuth2; _clientId = clientId; _clientSecret = clientSecret; _tokenUrl = tokenUrl; return this; }
    public McpAuthConfigBuilder WithScope(string scope) { _scopes ??= new List<string>(); _scopes.Add(scope); return this; }
    public McpAuthConfigBuilder WithScopes(params string[] scopes) { _scopes = scopes.ToList(); return this; }

    public McpAuthConfig Build() => new()
    {
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
