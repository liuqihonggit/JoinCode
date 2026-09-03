namespace McpClient;

public sealed class McpFallbackClient : McpClientBase
{
    private readonly McpServerConnectionConfig _config;
    private readonly McpTransportFallbackChain _chain;

    public McpFallbackClient(
        McpServerConnectionConfig config,
        (IMcpTransport[] Transports, ITransportHealthCheck[] HealthChecks) chainSpec,
        TransportFallbackConfig? fallbackConfig = null,
        ILogger? logger = null)
        : base(new McpClientOptions(), logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ServerName = _config.Name;

        _chain = new McpTransportFallbackChain(
            chainSpec.Transports, chainSpec.HealthChecks,
            fallbackConfig ?? TransportFallbackConfig.FromEnvironment(), logger);

        _chain.MessageReceived += OnChainMessageReceived;
        _chain.ErrorOccurred += OnChainError;
        _chain.FallbackOccurred += OnChainFallback;
    }

    public McpTransportFallbackChain Chain => _chain;

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger?.LogWarning("MCP fallback client already connected");
            return;
        }

        _logger?.LogInformation("Connecting to MCP server with fallback chain: {ServerName}", _config.Name);

        try
        {
            await _chain.StartAsync(cancellationToken).ConfigureAwait(false);
            await PerformHandshakeAsync(cancellationToken).ConfigureAwait(false);
            IsConnected = true;

            _logger?.LogInformation("MCP fallback client connected via {TransportType}", _chain.ActiveTransportType);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "MCP fallback client connection failed");
            await _chain.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;

        _logger?.LogInformation("Disconnecting MCP fallback client...");
        await _chain.StopAsync(cancellationToken).ConfigureAwait(false);
        IsConnected = false;
        await CancelPendingRequestsAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<JsonRpcResponse>();
        int requestId = request.GetIdAsInt();

        var guard = await _requestLock.LockAsync(cancellationToken);
        try
        {
            _pendingRequests[requestId] = tcs;
        }
        finally
        {
            guard.Dispose();
        }

        try
        {
            await _chain.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);

            using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            var guard1 = await _requestLock.LockAsync(cancellationToken);
            try
            {
                _pendingRequests.Remove(requestId);
            }
            finally
            {
                guard1.Dispose();
            }
            throw;
        }
    }

    protected override async Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        await _chain.SendMessageAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    private void OnChainMessageReceived(object? sender, McpMessageReceivedEventArgs e)
    {
        switch (e.Message)
        {
            case JsonRpcResponse response:
                _ = FireAndForgetProcessResponseAsync(response);
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

    private void OnChainError(object? sender, McpTransportErrorEventArgs e)
    {
        _logger?.LogError(e.Exception, "Fallback chain transport error (active={TransportType})", _chain.ActiveTransportType);

        if (IsConnected)
        {
            OnConnectionLost(new McpConnectionLostEventArgs
            {
                ServerName = _config.Name,
                TransportType = _chain.ActiveTransportType,
                Error = e.Exception
            });
        }
    }

    private void OnChainFallback(object? sender, TransportFallbackEventArgs e)
    {
        _logger?.LogInformation("Transport fallback: {FromType} -> {ToType} (reason={Reason}, server={IsServer})",
            e.FromTransportType, e.ToTransportType, e.Reason, e.IsServerSide);
    }

    public override async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _chain.MessageReceived -= OnChainMessageReceived;
        _chain.ErrorOccurred -= OnChainError;
        _chain.FallbackOccurred -= OnChainFallback;
        await _chain.DisposeAsync().ConfigureAwait(false);
        _requestLock.Dispose();
    }
}
