namespace Core.Bridge;

/// <summary>
/// 传输管理器 - 协调连接管理和消息路由
/// 作为 ConnectionManager 和 StringMessageRouter 的外观
/// </summary>
[Register(typeof(ITransportManager), ServiceLifetime.Singleton)]
public sealed partial class TransportManager : ITransportManager
{
    private readonly IConnectionManager _connectionManager;
    private readonly IMessageRouter _messageRouter;
    private readonly ILogger<TransportManager>? _logger;
    private int _disposed;

    /// <summary>当前连接状态</summary>
    public TransportConnectionState ConnectionState => _connectionManager.ConnectionState;
    /// <summary>当前传输协议</summary>
    public TransportProtocol CurrentProtocol => _connectionManager.CurrentProtocol;
    /// <summary>是否已连接</summary>
    public bool IsConnected => _connectionManager.IsConnected;
    /// <summary>重连尝试次数</summary>
    public int ReconnectAttemptCount => _connectionManager.ReconnectAttemptCount;

    /// <summary>接收到 Bridge 消息时触发</summary>
    public event EventHandler<BridgeMessageReceivedEventArgs>? MessageReceived;
    /// <summary>连接状态变更时触发</summary>
    public event EventHandler<StateChangedEventArgs<TransportConnectionState>>? ConnectionStateChanged
    {
        add => _connectionManager.ConnectionStateChanged += value;
        remove => _connectionManager.ConnectionStateChanged -= value;
    }
    /// <summary>传输错误发生时触发</summary>
    public event EventHandler<TransportErrorEventArgs>? ErrorOccurred
    {
        add => _connectionManager.ErrorOccurred += value;
        remove => _connectionManager.ErrorOccurred -= value;
    }
    /// <summary>开始重连时触发</summary>
    public event EventHandler? Reconnecting
    {
        add => _connectionManager.Reconnecting += value;
        remove => _connectionManager.Reconnecting -= value;
    }
    /// <summary>重连成功时触发</summary>
    public event EventHandler? Reconnected
    {
        add => _connectionManager.Reconnected += value;
        remove => _connectionManager.Reconnected -= value;
    }

    /// <summary>
    /// 构造传输管理器 — 连接 ConnectionManager 消息流到 StringMessageRouter 去重管道
    /// </summary>
    /// <param name="connectionManager">连接管理器</param>
    /// <param name="messageRouter">消息路由器</param>
    /// <param name="logger">日志记录器（可选）</param>
    public TransportManager(
        IConnectionManager connectionManager,
        IMessageRouter messageRouter,
        ILogger<TransportManager>? logger = null)
    {
        _logger = logger;
        _connectionManager = connectionManager;
        _messageRouter = messageRouter;

        // 连接消息流
        _connectionManager.OnMessageReceived(ProcessTransportMessageAsync);
        _messageRouter.MessageReceived += OnStringMessageReceived;
    }

    /// <summary>
    /// 启动传输连接
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
        => _connectionManager.StartAsync(cancellationToken);

    /// <summary>
    /// 停止传输连接
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
        => _connectionManager.StopAsync(cancellationToken);

    /// <summary>
    /// 发送消息
    /// </summary>
    public async Task SendMessageAsync(BridgeMessage message, CancellationToken cancellationToken = default)
    {
        var json = message.ToJson();
        await _connectionManager.SendMessageAsync(json, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("[TransportManager] 消息已发送: {MessageType}", message.Type);
    }

    /// <summary>
    /// 切换传输协议
    /// </summary>
    public Task SwitchProtocolAsync(TransportProtocol protocol, CancellationToken cancellationToken = default)
        => _connectionManager.SwitchProtocolAsync(protocol, cancellationToken);

    /// <summary>
    /// 处理传输层消息 — 委托给 StringMessageRouter 进行去重
    /// </summary>
    private Task ProcessTransportMessageAsync(string messageJson)
        => _messageRouter.ProcessMessageAsync(messageJson, ExtractMessageId);

    /// <summary>
    /// 从 JSON 消息中提取消息 ID
    /// </summary>
    private static string? ExtractMessageId(string messageJson)
    {
        try
        {
            var node = JsonNode.Parse(messageJson);
            if (node is not JsonObject obj)
                return null;

            if (obj.TryGetPropertyValue("id", out var idNode))
                return idNode?.GetValue<string>();

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 处理去重后的字符串消息 — 反序列化为 BridgeMessage 并分发
    /// </summary>
    private void OnStringMessageReceived(object? sender, StringMessageReceivedEventArgs e)
    {
        try
        {
            var message = BridgeMessageSerialization.FromJson(e.MessageJson);

            if (message is null)
            {
                _logger?.LogWarning("[TransportManager] 无法解析消息: {Message}", e.MessageJson);
                return;
            }

            // 过滤 Echo 消息
            if (message is EchoMessage)
            {
                _logger?.LogDebug("[TransportManager] 过滤 Echo 消息: {MessageId}", e.MessageId);
                return;
            }

            MessageReceived?.Invoke(this, new BridgeMessageReceivedEventArgs(message));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[TransportManager] 反序列化消息失败");
        }
    }

    /// <summary>
    /// 异步释放传输管理器资源
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _connectionManager.DisposeAsync().ConfigureAwait(false);
        await _messageRouter.DisposeAsync().ConfigureAwait(false);
    }
}

// TransportConfiguration, WebSocketTransport, SseBridgeTransport 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Impl)
// ConnectionManager 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Impl)
// MessageRouter, BoundedMessageIdSet 已迁移为 StringMessageRouter (JoinCode.Transport.Bridge 命名空间, Transport.Impl)

// IBridgeTransport 已迁移到 JoinCode.Transport 命名空间 (Transport.Contracts)

// BridgeMessageReceivedEventArgs 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)
