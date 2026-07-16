
namespace McpClient;

public abstract class McpNetworkClient<TTransport> : McpClientBase
    where TTransport : Transports.IMcpTransport
{
    private readonly McpServerConnectionConfig _config;
    private readonly TTransport _transport;

    protected abstract string TransportTypeName { get; }

    protected McpNetworkClient(
        McpServerConnectionConfig config,
        McpClientOptions? options,
        ILogger? logger,
        IMcpAuthProvider? authProvider,
        TTransport transport)
        : base(options ?? new McpClientOptions(), logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ServerName = _config.Name;
        _transport = transport;
        _transport.MessageReceived += OnTransportMessageReceived;
        _transport.ErrorOccurred += OnTransportError;
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger?.LogWarning("MCP {TransportType} 客户端已连接", TransportTypeName);
            return;
        }

        _logger?.LogInformation("正在连接到 MCP {TransportType} 服务器: {ServerName}", TransportTypeName, _config.Name);

        try
        {
            await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
            await PerformHandshakeAsync(cancellationToken).ConfigureAwait(false);

            IsConnected = true;
            _logger?.LogInformation("MCP {TransportType} 客户端连接成功", TransportTypeName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "连接 MCP {TransportType} 服务器失败", TransportTypeName);
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

        _logger?.LogInformation("正在断开 MCP {TransportType} 客户端连接...", TransportTypeName);

        await _transport.StopAsync(cancellationToken).ConfigureAwait(false);
        IsConnected = false;

        await CancelPendingRequestsAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("MCP {TransportType} 客户端已断开连接", TransportTypeName);
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

    private void OnTransportError(object? sender, Transports.McpTransportErrorEventArgs e)
    {
        _logger?.LogError(e.Exception, "{TransportType} 传输错误", TransportTypeName);

        if (IsConnected)
        {
            OnConnectionLost(new McpConnectionLostEventArgs
            {
                ServerName = _config.Name,
                TransportType = TransportTypeName,
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
