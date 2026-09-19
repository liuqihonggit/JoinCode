
namespace McpClient;

/// <summary>
/// MCP 网络客户端抽象基类 — 封装基于 IMcpTransport 的通用连接、断开、请求/通知发送与消息分发逻辑。
/// 派生类通过指定具体 TTransport 类型复用全部网络交互流程。
/// </summary>
/// <typeparam name="TTransport">传输实现类型,必须实现 IMcpTransport。</typeparam>
public abstract class McpNetworkClient<TTransport> : McpClientBase
    where TTransport : Transports.IMcpTransport {
    private readonly McpServerConnectionConfig _config;
    private readonly TTransport _transport;

    /// <summary>传输类型名称,由派生类提供,用于日志与事件标识。</summary>
    protected abstract string TransportTypeName { get; }

    /// <summary>构造 McpNetworkClient 实例 — 绑定传输层并订阅消息/错误事件。</summary>
    /// <param name="config">服务器连接配置。</param>
    /// <param name="options">客户端选项,为 null 时使用默认值。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="authProvider">认证提供者,可为 null。</param>
    /// <param name="transport">传输实现实例。</param>
    protected McpNetworkClient(
        McpServerConnectionConfig config,
        McpClientOptions? options,
        ILogger? logger,
        IMcpAuthProvider? authProvider,
        TTransport transport)
        : base(options ?? new McpClientOptions(), logger) {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ServerName = _config.Name;
        _transport = transport;
        _transport.MessageReceived += OnTransportMessageReceived;
        _transport.ErrorOccurred += OnTransportError;
    }

    /// <summary>
    /// 异步连接到 MCP 服务器 — 启动传输层并执行 MCP 握手。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步连接操作的任务。</returns>
    public override async Task ConnectAsync(CancellationToken cancellationToken = default) {
        if (IsConnected) {
            _logger?.LogWarning("MCP {TransportType} 客户端已连接", TransportTypeName);
            return;
        }

        _logger?.LogInformation("正在连接到 MCP {TransportType} 服务器: {ServerName}", TransportTypeName, _config.Name);

        try {
            await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
            await PerformHandshakeAsync(cancellationToken).ConfigureAwait(false);

            IsConnected = true;
            _logger?.LogInformation("MCP {TransportType} 客户端连接成功", TransportTypeName);
        } catch (Exception ex) {
            _logger?.LogError(ex, "连接 MCP {TransportType} 服务器失败", TransportTypeName);
            await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 异步断开与 MCP 服务器的连接 — 停止传输层并取消所有 pending 请求。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步断开操作的任务。</returns>
    public override async Task DisconnectAsync(CancellationToken cancellationToken = default) {
        if (!IsConnected) {
            return;
        }

        _logger?.LogInformation("正在断开 MCP {TransportType} 客户端连接...", TransportTypeName);

        await _transport.StopAsync(cancellationToken).ConfigureAwait(false);
        IsConnected = false;

        await CancelPendingRequestsAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("MCP {TransportType} 客户端已断开连接", TransportTypeName);
    }

    /// <summary>异步发送 JSON-RPC 请求 — 注册 pending request 并通过传输层发送,等待响应或超时。</summary>
    /// <param name="request">JSON-RPC 请求对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>服务器返回的 JSON-RPC 响应。</returns>
    protected override async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken) {
        var tcs = new TaskCompletionSource<JsonRpcResponse>();
        var requestId = request.GetIdAsInt();

        await _requestRegistry.RegisterAsync(requestId, tcs, cancellationToken).ConfigureAwait(false);

        try {
            await _transport.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);

            using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        } catch {
            await _requestRegistry.RemoveAsync(requestId, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>异步发送 JSON-RPC 通知 — 通过传输层发送,无需响应。</summary>
    /// <param name="notification">JSON-RPC 通知对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected override async Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken) {
        await _transport.SendMessageAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    private void OnTransportMessageReceived(object? sender, Transports.McpMessageReceivedEventArgs e) {
        switch (e.Message) {
            case JsonRpcResponse response:
            _ = FireAndForgetProcessResponseAsync(response);
            break;
            case JsonRpcNotification notification:
            OnNotificationReceived(new McpNotificationReceivedEventArgs {
                Method = notification.Method,
                Params = notification.Params
            });
            break;
            case JsonRpcRequest request:
            _ = HandleServerRequestAsync(request, CancellationToken.None);
            break;
        }
    }

    private void OnTransportError(object? sender, Transports.McpTransportErrorEventArgs e) {
        _logger?.LogError(e.Exception, "{TransportType} 传输错误", TransportTypeName);

        if (IsConnected) {
            OnConnectionLost(new McpConnectionLostEventArgs {
                ServerName = _config.Name,
                TransportType = TransportTypeName,
                Error = e.Exception
            });
        }
    }

    /// <summary>
    /// 异步释放客户端资源 — 异步断开连接、解绑传输事件并异步释放传输层与请求注册表。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public override async ValueTask DisposeAsync() {
        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _transport.MessageReceived -= OnTransportMessageReceived;
        _transport.ErrorOccurred -= OnTransportError;
        await _transport.DisposeAsync().ConfigureAwait(false);
        await _requestRegistry.DisposeAsync().ConfigureAwait(false);
    }
}