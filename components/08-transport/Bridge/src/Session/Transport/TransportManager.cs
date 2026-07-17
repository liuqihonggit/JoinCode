using JoinCode.Abstractions.Attributes;

namespace Core.Bridge;

/// <summary>
/// 传输管理器 - 协调连接管理和消息路由
/// 作为 ConnectionManager 和 StringMessageRouter 的外观
/// </summary>
[Register]
public sealed partial class TransportManager : ITransportManager
{
    private readonly IConnectionManager _connectionManager;
    private readonly IMessageRouter _messageRouter;
    [Inject] private readonly ILogger<TransportManager>? _logger;

    public TransportConnectionState ConnectionState => _connectionManager.ConnectionState;
    public TransportProtocol CurrentProtocol => _connectionManager.CurrentProtocol;
    public bool IsConnected => _connectionManager.IsConnected;
    public int ReconnectAttemptCount => _connectionManager.ReconnectAttemptCount;

    public event EventHandler<BridgeMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<StateChangedEventArgs<TransportConnectionState>>? ConnectionStateChanged
    {
        add => _connectionManager.ConnectionStateChanged += value;
        remove => _connectionManager.ConnectionStateChanged -= value;
    }
    public event EventHandler<TransportErrorEventArgs>? ErrorOccurred
    {
        add => _connectionManager.ErrorOccurred += value;
        remove => _connectionManager.ErrorOccurred -= value;
    }
    public event EventHandler? Reconnecting
    {
        add => _connectionManager.Reconnecting += value;
        remove => _connectionManager.Reconnecting -= value;
    }
    public event EventHandler? Reconnected
    {
        add => _connectionManager.Reconnected += value;
        remove => _connectionManager.Reconnected -= value;
    }

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

    public async ValueTask DisposeAsync()
    {
        await _connectionManager.DisposeAsync().ConfigureAwait(false);
        await _messageRouter.DisposeAsync().ConfigureAwait(false);
    }
}

// TransportConfiguration, WebSocketTransport, SseBridgeTransport 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Impl)
// ConnectionManager 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Impl)
// MessageRouter, BoundedMessageIdSet 已迁移为 StringMessageRouter (JoinCode.Transport.Bridge 命名空间, Transport.Impl)

// IBridgeTransport 已迁移到 JoinCode.Transport 命名空间 (Transport.Contracts)

// BridgeMessageReceivedEventArgs 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)
