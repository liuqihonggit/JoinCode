
namespace McpClient;

public sealed partial class McpWebSocketClient : McpClientBase
{
    private readonly McpServerConnectionConfig _config;
    private readonly Transports.WebSocketTransport _transport;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly Dictionary<int, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();

    public McpWebSocketClient(McpServerConnectionConfig config, McpClientOptions? options = null, ILogger? logger = null, IMcpAuthProvider? authProvider = null)
        : base(options ?? new McpClientOptions(), logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ServerName = _config.Name;

        IMcpAuthProvider? resolvedAuthProvider = authProvider;
        if (resolvedAuthProvider == null && _config.Auth != null)
        {
            resolvedAuthProvider = McpAuthProviderFactory.Create(_config.Auth, logger);
        }

        _transport = new Transports.WebSocketTransport(_config, resolvedAuthProvider, logger as ILogger<Transports.WebSocketTransport>);
        _transport.MessageReceived += OnTransportMessageReceived;
        _transport.ErrorOccurred += OnTransportError;
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger?.LogWarning("MCP WebSocket 客户端已连接");
            return;
        }

        _logger?.LogInformation("正在连接到 MCP WebSocket 服务器: {ServerName}", _config.Name);

        try
        {
            await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
            await PerformHandshakeAsync(cancellationToken).ConfigureAwait(false);

            IsConnected = true;
            _logger?.LogInformation("MCP WebSocket 客户端连接成功");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "连接 MCP WebSocket 服务器失败");
            await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        _logger?.LogInformation("正在断开 MCP WebSocket 客户端连接...");

        await _transport.StopAsync(cancellationToken).ConfigureAwait(false);
        IsConnected = false;

        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var tcs in _pendingRequests.Values)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            _pendingRequests.Clear();
        }
        finally
        {
            _requestLock.Release();
        }

        _logger?.LogInformation("MCP WebSocket 客户端已断开连接");
    }

    protected override async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<JsonRpcResponse>();
        int requestId = request.GetIdAsInt();

        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pendingRequests[requestId] = tcs;
        }
        finally
        {
            _requestLock.Release();
        }

        try
        {
            await _transport.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);

            using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _pendingRequests.Remove(requestId);
            }
            finally
            {
                _requestLock.Release();
            }

            throw;
        }
    }

    protected override async Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        await _transport.SendMessageAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    private void OnTransportMessageReceived(object? sender, Transports.McpMessageReceivedEventArgs e)
    {
        switch (e.Message)
        {
            case JsonRpcResponse response:
                ProcessResponseAsync(response).ConfigureAwait(false);
                break;
            case JsonRpcNotification notification:
                OnNotificationReceived(new McpNotificationReceivedEventArgs
                {
                    Method = notification.Method,
                    Params = notification.Params
                });
                break;
            case JsonRpcRequest request:
                _ = HandleServerRequestAsync(request, CancellationToken.None);
                break;
        }
    }

    private async Task ProcessResponseAsync(JsonRpcResponse response)
    {
        if (response.Id == null) return;

        int requestId = response.GetIdAsInt();

        await _requestLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_pendingRequests.TryGetValue(requestId, out var tcs))
            {
                tcs.TrySetResult(response);
                _pendingRequests.Remove(requestId);
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private void OnTransportError(object? sender, Transports.McpTransportErrorEventArgs e)
    {
        _logger?.LogError(e.Exception, "WebSocket 传输错误");

        if (IsConnected)
        {
            OnConnectionLost(new McpConnectionLostEventArgs
            {
                ServerName = _config.Name,
                TransportType = "websocket",
                Error = e.Exception
            });
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _transport.MessageReceived -= OnTransportMessageReceived;
        _transport.ErrorOccurred -= OnTransportError;
        await _transport.DisposeAsync().ConfigureAwait(false);
        _requestLock.Dispose();
    }
}