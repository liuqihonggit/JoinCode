
namespace McpClient.Transports;

/// <summary>
/// HTTP (Streamable HTTP) 客户端传输 — 继承 TransportBase 共享连接管理内核，实现 IMcpTransport 桥接 JSON-RPC 协议
/// 对齐 TS StreamableHTTPClientTransport: 通过 HTTP POST 发送 JSON-RPC 消息，支持 SSE 响应流
/// </summary>
public sealed partial class HttpTransport : TransportBase, IMcpTransport
{
    private readonly HttpTransportOptions _options;
    [Inject] private readonly ILogger<HttpTransport>? _logger;
    private readonly HttpClient _httpClient;
    private readonly IMcpAuthProvider? _authProvider;
    private string? _sessionId;

    private const int PostTimeoutMs = 60000;
    private const string StreamableHttpAccept = "application/json, text/event-stream";

    /// <summary>IMcpTransport: 收到 JSON-RPC 消息</summary>
    public event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;

    /// <summary>IMcpTransport: 传输错误（隐藏基类同名事件，使用 MCP 专用参数类型）</summary>
    public new event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;

    /// <summary>Step-Up 认证检测事件</summary>
    public event EventHandler<StepUpDetectedEventArgs>? StepUpDetected;

    public HttpTransport(HttpTransportOptions options, IMcpAuthProvider? authProvider = null, ILogger<HttpTransport>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _authProvider = authProvider;
        _logger = logger;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(PostTimeoutMs)
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "JoinCode-MCP/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", StreamableHttpAccept);

        // 订阅基类事件，桥接到 MCP 事件
        base.ErrorOccurred += (_, e) =>
            ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs { Exception = e.Exception });
    }

    public HttpTransport(McpServerConnectionConfig config, IMcpAuthProvider? authProvider = null, ILogger<HttpTransport>? logger = null)
        : this(CreateOptionsFromConfig(config), authProvider, logger)
    {
    }

    /// <inheritdoc/>
    protected override Task ConnectCoreAsync(CancellationToken ct)
    {
        // HTTP 传输不需要初始连接，首次 SendMessage 时建立
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task DisconnectCoreAsync(CancellationToken ct)
    {
        _sessionId = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override async Task SendCoreAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException(McpErrorMessages.TransportNotRunning);
        }

        var json = Encoding.UTF8.GetString(payload.Span);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (!string.IsNullOrEmpty(_sessionId))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        var combinedHeaders = await GetCombinedHeadersAsync(ct).ConfigureAwait(false);
        foreach (var (key, value) in combinedHeaders)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _sessionId = null;
            _logger?.LogWarning("MCP 会话过期，下次请求将重新建立");
        }

        var stepUpScope = StepUpDetector.DetectStepUp(response, _authProvider);
        if (stepUpScope is not null)
        {
            _logger?.LogWarning("Step-Up 认证检测: 需要 scope={Scope}", stepUpScope);
            StepUpDetected?.Invoke(this, new StepUpDetectedEventArgs { Scope = stepUpScope });
        }

        response.EnsureSuccessStatusCode();

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
        {
            _sessionId = sessionIds.FirstOrDefault();
            _logger?.LogDebug("MCP 会话 ID: {SessionId}", _sessionId);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;

        if (contentType is not null && contentType.Contains("text/event-stream"))
        {
            BackgroundTask = ProcessSseResponseAsync(response, ct);
        }
        else
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                var responseMessage = ParseMessage(responseBody);
                if (responseMessage is not null)
                {
                    OnPayloadReceived(Encoding.UTF8.GetBytes(responseBody));
                    MessageReceived?.Invoke(this, new McpMessageReceivedEventArgs { Message = responseMessage });
                }
            }
        }
    }

    /// <summary>IMcpTransport: 发送 JSON-RPC 消息（序列化为字节后委托给基类）</summary>
    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var json = message.ToJson();
        var bytes = Encoding.UTF8.GetBytes(json);
        await SendAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return Task.CompletedTask;

        CreateCtsAndToken();
        IsRunning = true;

        _logger?.LogInformation("启动 HTTP 传输: {Endpoint}", _options.Endpoint);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return;
        IsRunning = false;
        await GracefulStopAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("HTTP 传输已停止");
    }

    /// <summary>
    /// 处理 SSE 响应流 — 使用 SseStreamParser 解析事件
    /// </summary>
    private async Task ProcessSseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var sseEvent in SseStreamParser.ParseAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                var message = ParseMessage(sseEvent.Data);
                if (message is not null)
                {
                    OnPayloadReceived(Encoding.UTF8.GetBytes(sseEvent.Data));
                    MessageReceived?.Invoke(this, new McpMessageReceivedEventArgs { Message = message });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SSE 响应流处理异常");
            OnErrorOccurred(ex);
        }
    }

    private static JsonRpcMessage? ParseMessage(string json)
    {
        try
        {
            return McpMessageExtensions.FromJson(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Dictionary<string, string>> GetCombinedHeadersAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.HeadersHelper))
        {
            return _options.Headers;
        }

        var dynamicHeaders = await McpHeadersHelper.GetDynamicHeadersAsync(
            _options.ServerName, _options.ServerUrl, _options.HeadersHelper, _logger, cancellationToken).ConfigureAwait(false);

        return McpHeadersHelper.CombineHeaders(_options.Headers, dynamicHeaders);
    }

    private static HttpTransportOptions CreateOptionsFromConfig(McpServerConnectionConfig config)
    {
        var headers = new Dictionary<string, string>();

        if (config.Headers is not null)
        {
            foreach (var kvp in config.Headers)
            {
                headers[kvp.Key] = kvp.Value;
            }
        }

        if (config.Auth is not null)
        {
            switch (config.Auth.Type)
            {
                case McpAuthType.Bearer when !string.IsNullOrEmpty(config.Auth.BearerToken):
                    headers["Authorization"] = $"Bearer {config.Auth.BearerToken}";
                    break;
                case McpAuthType.ApiKey when !string.IsNullOrEmpty(config.Auth.ApiKey):
                    headers["X-API-Key"] = config.Auth.ApiKey;
                    break;
                case McpAuthType.Basic when !string.IsNullOrEmpty(config.Auth.Username):
                    var credentials = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes($"{config.Auth.Username}:{config.Auth.Password}"));
                    headers["Authorization"] = $"Basic {credentials}";
                    break;
            }
        }

        return new HttpTransportOptions
        {
            Name = config.Name,
            Endpoint = config.Endpoint,
            Headers = headers,
            HeadersHelper = config.HeadersHelper,
            ServerName = config.Name,
            ServerUrl = config.Endpoint
        };
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _httpClient.Dispose();
    }
}

/// <summary>
/// HTTP 传输选项
/// </summary>
public sealed partial class HttpTransportOptions
{
    /// <summary>传输名称</summary>
    public string Name { get; init; } = McpClientTransportTypeConstants.Http;

    /// <summary>端点 URL</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>自定义请求头（静态 + Auth 合并后的头）</summary>
    public Dictionary<string, string> Headers { get; init; } = new();

    /// <summary>动态请求头获取脚本 — 对齐 TS headersHelper</summary>
    public string? HeadersHelper { get; init; }

    /// <summary>服务器名称 — 传递给 headersHelper 的 CLAUDE_CODE_MCP_SERVER_NAME 环境变量</summary>
    public string ServerName { get; init; } = string.Empty;

    /// <summary>服务器 URL — 传递给 headersHelper 的 CLAUDE_CODE_MCP_SERVER_URL 环境变量</summary>
    public string ServerUrl { get; init; } = string.Empty;
}
