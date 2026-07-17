namespace JoinCode.Transport.Bridge;

/// <summary>
/// 连接管理器接口 — 管理传输连接生命周期和重连逻辑
/// </summary>
public interface IConnectionManager : IAsyncDisposable
{
    /// <summary>当前连接状态</summary>
    TransportConnectionState ConnectionState { get; }

    /// <summary>当前传输协议</summary>
    TransportProtocol CurrentProtocol { get; }

    /// <summary>是否已连接</summary>
    bool IsConnected { get; }

    /// <summary>重连尝试次数</summary>
    int ReconnectAttemptCount { get; }

    /// <summary>连接状态变更事件</summary>
    event EventHandler<StateChangedEventArgs<TransportConnectionState>>? ConnectionStateChanged;

    /// <summary>正在重连事件</summary>
    event EventHandler? Reconnecting;

    /// <summary>重连成功事件</summary>
    event EventHandler? Reconnected;

    /// <summary>传输错误事件</summary>
    event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

    /// <summary>启动连接</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>停止连接</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>切换传输协议</summary>
    Task SwitchProtocolAsync(TransportProtocol protocol, CancellationToken cancellationToken = default);

    /// <summary>发送消息</summary>
    Task SendMessageAsync(string messageJson, CancellationToken cancellationToken = default);

    /// <summary>注册消息接收回调</summary>
    void OnMessageReceived(Func<string, Task> handler);
}
