namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输管理器接口 — 协调连接管理和消息路由
/// 作为 ConnectionManager 和 MessageRouter 的外观
/// </summary>
public interface ITransportManager : IAsyncDisposable
{
    /// <summary>当前连接状态</summary>
    TransportConnectionState ConnectionState { get; }

    /// <summary>当前传输协议</summary>
    TransportProtocol CurrentProtocol { get; }

    /// <summary>是否已连接</summary>
    bool IsConnected { get; }

    /// <summary>重连尝试次数</summary>
    int ReconnectAttemptCount { get; }

    /// <summary>收到 Bridge 消息事件</summary>
    event EventHandler<BridgeMessageReceivedEventArgs>? MessageReceived;

    /// <summary>连接状态变更事件</summary>
    event EventHandler<StateChangedEventArgs<TransportConnectionState>>? ConnectionStateChanged;

    /// <summary>发生错误事件</summary>
    event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

    /// <summary>正在重连事件</summary>
    event EventHandler? Reconnecting;

    /// <summary>重连成功事件</summary>
    event EventHandler? Reconnected;

    /// <summary>启动传输管理器</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>停止传输管理器</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送 Bridge 消息
    /// </summary>
    /// <param name="message">要发送的 Bridge 消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SendMessageAsync(BridgeMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 切换传输协议
    /// </summary>
    /// <param name="protocol">目标传输协议</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SwitchProtocolAsync(TransportProtocol protocol, CancellationToken cancellationToken = default);
}
