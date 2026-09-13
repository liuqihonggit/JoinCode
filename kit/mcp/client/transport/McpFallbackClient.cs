namespace McpClient;

/// <summary>
/// MCP 回退客户端 — 基于传输回退链管理多个备用传输,主传输故障时自动切换到健康备用传输。
/// </summary>
public sealed class McpFallbackClient : McpClientBase
{
    private readonly McpServerConnectionConfig _config;
    private readonly McpTransportFallbackChain _chain;

    /// <summary>
    /// 构造 McpFallbackClient 实例。
    /// </summary>
    /// <param name="config">服务器连接配置。</param>
    /// <param name="chainSpec">回退链规格,包含传输数组与健康检查数组。</param>
    /// <param name="fallbackConfig">回退策略配置,为 null 时从环境变量读取。</param>
    /// <param name="logger">日志记录器。</param>
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

    /// <summary>底层传输回退链实例 — 暴露用于诊断与事件订阅。</summary>
    public McpTransportFallbackChain Chain => _chain;

    /// <summary>
    /// 异步连接到 MCP 服务器 — 启动回退链并执行 MCP 握手。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步连接操作的任务。</returns>
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

    /// <summary>
    /// 异步断开与 MCP 服务器的连接 — 停止回退链并取消所有 pending 请求。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步断开操作的任务。</returns>
    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;

        _logger?.LogInformation("Disconnecting MCP fallback client...");
        await _chain.StopAsync(cancellationToken).ConfigureAwait(false);
        IsConnected = false;
        await CancelPendingRequestsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>异步发送 JSON-RPC 请求 — 通过回退链发送,注册 pending request 并等待响应或超时。</summary>
    /// <param name="request">JSON-RPC 请求对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>服务器返回的 JSON-RPC 响应。</returns>
    protected override async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<JsonRpcResponse>();
        int requestId = request.GetIdAsInt();

        await _requestRegistry.RegisterAsync(requestId, tcs, cancellationToken).ConfigureAwait(false);

        try
        {
            await _chain.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);

            using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            await _requestRegistry.RemoveAsync(requestId, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>异步发送 JSON-RPC 通知 — 通过回退链发送,无需响应。</summary>
    /// <param name="notification">JSON-RPC 通知对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
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

    /// <summary>
    /// 异步释放客户端资源 — 断开连接、解绑回退链事件并释放回退链与请求注册表。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public override async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _chain.MessageReceived -= OnChainMessageReceived;
        _chain.ErrorOccurred -= OnChainError;
        _chain.FallbackOccurred -= OnChainFallback;
        await _chain.DisposeAsync().ConfigureAwait(false);
        await _requestRegistry.DisposeAsync().ConfigureAwait(false);
    }
}
