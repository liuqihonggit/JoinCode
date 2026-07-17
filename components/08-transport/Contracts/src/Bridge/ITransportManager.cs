namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输管理器接口 — 协调连接管理和消息路由
/// 作为 ConnectionManager 和 MessageRouter 的外观
/// </summary>
public interface ITransportManager : IAsyncDisposable
{
    TransportConnectionState ConnectionState { get; }
    TransportProtocol CurrentProtocol { get; }
    bool IsConnected { get; }
    int ReconnectAttemptCount { get; }

    event EventHandler<BridgeMessageReceivedEventArgs>? MessageReceived;
    event EventHandler<StateChangedEventArgs<TransportConnectionState>>? ConnectionStateChanged;
    event EventHandler<TransportErrorEventArgs>? ErrorOccurred;
    event EventHandler? Reconnecting;
    event EventHandler? Reconnected;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SendMessageAsync(BridgeMessage message, CancellationToken cancellationToken = default);
    Task SwitchProtocolAsync(TransportProtocol protocol, CancellationToken cancellationToken = default);
}
