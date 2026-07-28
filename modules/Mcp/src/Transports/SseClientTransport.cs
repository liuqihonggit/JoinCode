
namespace McpClient.Transports;

/// <summary>
/// SSE 客户端传输 — 继承 TransportBase 共享连接管理内核，实现 IMcpTransport 桥接 JSON-RPC 协议
/// 对齐 TS SSEClientTransport: 连接到远程 MCP 服务器的 SSE 端点，接收事件流并通过 POST 发送消息
/// </summary>
public sealed partial class SseClientTransport : TransportBase, IMcpTransport
{
    private readonly SseTransportOptions _options;
    [Inject] private readonly ILogger<SseClientTransport>? _logger;
    private readonly HttpClient _httpClient;
    private readonly IMcpAuthProvider? _authProvider;
    private readonly ReconnectPolicy _reconnectPolicy = new()
    {
        MaxAttempts = 5,
        InitialBackoffMs = 1000,
        MaxBackoffMs = 30000
    };
    private string? _messageEndpoint;
    private int _reconnectAttempts;

    private const int PostTimeoutMs = 60000;

    /// <summary>IMcpTransport: 收到 JSON-RPC 消息</summary>
    public event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;

    /// <summary>IMcpTransport: 传输错误（隐藏基类同名事件，使用 MCP 专用参数类型）</summary>
    public new event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Step-Up 认证检测事件 — 对齐 TS wrapFetchWithStepUpDetection
    /// </summary>
    public event EventHandler<StepUpDetectedEventArgs>? StepUpDetected;

    public SseClientTransport(SseTransportOptions options, IMcpAuthProvider? authProvider = null, ILogger<SseClientTransport>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _authProvider = authProvider;
        _logger = logger;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(PostTimeoutMs)
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "JoinCode-MCP/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/event-stream");

        // 订阅基类事件，桥接到 MCP 事件
        base.ErrorOccurred += (_, e) =>
            ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs { Exception = e.Exception });
    }

    public SseClientTransport(McpServerConnectionConfig config, IMcpAuthProvider? authProvider = null, ILogger<SseClientTransport>? logger = null)
        : this(CreateOptionsFromConfig(config), authProvider, logger)
    {
    }

    /// <inheritdoc/>
    protected override Task ConnectCoreAsync(CancellationToken ct)
    {
        // SSE 连接在后台任务中建立，此处无需操作
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task DisconnectCoreAsync(CancellationToken ct)
    {
        // SSE 断开由 GracefulStopAsync 处理
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override async Task SendCoreAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException(McpErrorMessages.TransportNotRunning);
        }

        if (string.IsNullOrEmpty(_messageEndpoint))
        {
            throw new InvalidOperationException("SSE 连接尚未建立消息端点");
        }

        var json = Encoding.UTF8.GetString(payload.Span);

        using var request = new HttpRequestMessage(HttpMethod.Post, _messageEndpoint);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var combinedHeaders = await GetCombinedHeadersAsync(ct).ConfigureAwait(false);
        foreach (var (key, value) in combinedHeaders)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        var stepUpScope = StepUpDetector.DetectStepUp(response, _authProvider);
        if (stepUpScope is not null)
        {
            _logger?.LogWarning("Step-Up 认证检测(POST): 需要 scope={Scope}", stepUpScope);
            StepUpDetected?.Invoke(this, new StepUpDetectedEventArgs { Scope = stepUpScope });
        }

        response.EnsureSuccessStatusCode();
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
    public override async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;

        var token = CreateCtsAndToken();
        BackgroundTask = ListenSseStreamAsync(token);
        IsRunning = true;
        _reconnectAttempts = 0;

        _logger?.LogInformation("启动 SSE 客户端传输: {Endpoint}", _options.Endpoint);
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return;
        IsRunning = false;
        await GracefulStopAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("SSE 客户端传输已停止");
    }

    /// <summary>
    /// 监听 SSE 事件流 — 对齐 TS SSEClientTransport 的 EventSource 连接
    /// </summary>
    private async Task ListenSseStreamAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsRunning)
        {
            try
            {
                await ConnectAndListenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogWarning(ex, "SSE 连接失败: {Endpoint}", _options.Endpoint);
                ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs { Exception = ex });

                _reconnectAttempts++;
                if (!await _reconnectPolicy.WaitAsync(_reconnectAttempts, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SSE 监听异常");
                ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs { Exception = ex });

                _reconnectAttempts++;
                if (!await _reconnectPolicy.WaitAsync(_reconnectAttempts, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        IsRunning = false;
        OnConnectionClosed();
    }

    /// <summary>
    /// 连接并监听 SSE 流 — 使用 SseStreamParser 解析事件
    /// </summary>
    private async Task ConnectAndListenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.Endpoint);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

        var combinedHeaders = await GetCombinedHeadersAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (key, value) in combinedHeaders)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        var stepUpScope = StepUpDetector.DetectStepUp(response, _authProvider);
        if (stepUpScope is not null)
        {
            _logger?.LogWarning("Step-Up 认证检测: 需要 scope={Scope}", stepUpScope);
            StepUpDetected?.Invoke(this, new StepUpDetectedEventArgs { Scope = stepUpScope });
            response.EnsureSuccessStatusCode();
        }

        response.EnsureSuccessStatusCode();

        _reconnectAttempts = 0;

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        // 使用 SseStreamParser 解析 SSE 事件流
        await foreach (var sseEvent in SseStreamParser.ParseAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            ProcessSseEvent(sseEvent.EventType, sseEvent.Data);
        }
    }

    /// <summary>
    /// 处理 SSE 事件
    /// </summary>
    private void ProcessSseEvent(string eventType, string data)
    {
        if (string.Equals(eventType, "endpoint", StringComparison.OrdinalIgnoreCase))
        {
            _messageEndpoint = data.Trim();
            _logger?.LogInformation("SSE 消息端点: {Endpoint}", _messageEndpoint);
            return;
        }

        var message = ParseMessage(data);
        if (message is not null)
        {
            // 通知基类收到字节载荷
            var payload = Encoding.UTF8.GetBytes(data);
            OnPayloadReceived(payload);

            MessageReceived?.Invoke(this, new McpMessageReceivedEventArgs { Message = message });
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

    private static SseTransportOptions CreateOptionsFromConfig(McpServerConnectionConfig config)
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

        return new SseTransportOptions
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
/// SSE 传输选项
/// </summary>
public sealed partial class SseTransportOptions
{
    /// <summary>传输名称</summary>
    public string Name { get; init; } = McpClientTransportTypeConstants.Sse;

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
