
namespace McpClient.Transports;

/// <summary>
/// WebSocket MCP 传输 — 继承 TransportBase 共享连接管理内核，实现 IMcpTransport 桥接 JSON-RPC 协议
/// </summary>
public sealed partial class WebSocketTransport : TransportBase, IMcpTransport
{
    private readonly McpServerConnectionConfig _config;
    private readonly IMcpAuthProvider? _authProvider;
    [Inject] private readonly ILogger<WebSocketTransport>? _logger;
    private readonly SemaphoreSlim _receiveLock = new(1, 1);
    private System.Net.WebSockets.ClientWebSocket? _ws;

    /// <summary>IMcpTransport: 收到 JSON-RPC 消息</summary>
    public event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;

    /// <summary>IMcpTransport: 传输错误（隐藏基类同名事件，使用 MCP 专用参数类型）</summary>
    public new event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;

    public WebSocketTransport(
        McpServerConnectionConfig config,
        IMcpAuthProvider? authProvider = null,
        ILogger<WebSocketTransport>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _authProvider = authProvider;
        _logger = logger;

        // 订阅基类事件，桥接到 MCP 事件
        base.ErrorOccurred += (_, e) =>
            ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs { Exception = e.Exception });
    }

    /// <inheritdoc/>
    protected override async Task ConnectCoreAsync(CancellationToken ct)
    {
        _logger?.LogInformation("正在连接 WebSocket MCP 服务器: {Url}", _config.Endpoint);

        _ws = new System.Net.WebSockets.ClientWebSocket();

        if (_config.Headers != null)
        {
            foreach (var (key, value) in _config.Headers)
            {
                _ws.Options.SetRequestHeader(key, value);
            }
        }

        if (_authProvider != null)
        {
            try
            {
                var token = await _authProvider.GetAccessTokenAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    _ws.Options.SetRequestHeader("Authorization", $"Bearer {token}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "获取认证令牌失败，尝试无认证连接");
            }
        }

        var uri = new Uri(_config.Endpoint);
        await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);

        var token2 = CreateCtsAndToken();
        BackgroundTask = Task.Run(() => ReceiveLoopAsync(token2), token2);

        _logger?.LogInformation("WebSocket MCP 服务器连接成功");
    }

    /// <inheritdoc/>
    protected override async Task DisconnectCoreAsync(CancellationToken ct)
    {
        _logger?.LogInformation("正在断开 WebSocket 连接...");

        if (_ws != null && _ws.State == System.Net.WebSockets.WebSocketState.Open)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                await _ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Client disconnecting", cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("关闭 WebSocket 时出错: {Error}", ex.Message);
            }
        }

        _logger?.LogInformation("WebSocket 连接已断开");
    }

    /// <inheritdoc/>
    protected override async Task SendCoreAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (_ws == null || _ws.State != System.Net.WebSockets.WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket 未连接");
        }

        await _ws.SendAsync(payload, System.Net.WebSockets.WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    /// <summary>IMcpTransport: 发送 JSON-RPC 消息（序列化为字节后委托给基类）</summary>
    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        var json = message.ToJson();
        var bytes = Encoding.UTF8.GetBytes(json);
        await SendAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _ws != null && _ws.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var segment = new ArraySegment<byte>(buffer);
                var result = await _ws.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);

                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                {
                    _logger?.LogInformation("WebSocket 服务器发起关闭");
                    break;
                }

                var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                messageBuilder.Append(chunk);

                if (result.EndOfMessage)
                {
                    var messageText = messageBuilder.ToString();
                    messageBuilder.Clear();

                    if (!string.IsNullOrWhiteSpace(messageText))
                    {
                        // 通知基类收到字节载荷
                        var payload = Encoding.UTF8.GetBytes(messageText);
                        OnPayloadReceived(payload);

                        // 桥接到 MCP 协议层
                        try
                        {
                            var rpcMessage = McpMessageExtensions.FromJson(messageText);
                            MessageReceived?.Invoke(this, new McpMessageReceivedEventArgs { Message = rpcMessage });
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "解析 WebSocket 消息失败: {Message}", messageText);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (System.Net.WebSockets.WebSocketException ex)
        {
            _logger?.LogError(ex, "WebSocket 接收异常");
            if (IsRunning)
            {
                OnErrorOccurred(ex);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "WebSocket 接收循环异常");
            if (IsRunning)
            {
                OnErrorOccurred(ex);
            }
        }
        finally
        {
            if (IsRunning)
            {
                IsRunning = false;
                OnConnectionClosed();
            }
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _ws?.Dispose();
        _receiveLock.Dispose();
    }
}
