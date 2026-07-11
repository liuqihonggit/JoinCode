namespace JoinCode.Transport.Bridge;

/// <summary>
/// Bridge 传输接口 — 底层传输抽象
/// </summary>
public interface IBridgeTransport
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SendAsync(string message, CancellationToken cancellationToken = default);
    event EventHandler<TransportMessageReceivedEventArgs>? MessageReceived;
    event EventHandler<TransportErrorEventArgs>? ErrorOccurred;
}

/// <summary>
/// 传输消息接收事件参数
/// </summary>
public sealed class TransportMessageReceivedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

/// <summary>
/// 传输状态变更事件参数
/// </summary>
public sealed class TransportStateChangedEventArgs(TransportConnectionState oldState, TransportConnectionState newState) : EventArgs
{
    public TransportConnectionState OldState { get; } = oldState;
    public TransportConnectionState NewState { get; } = newState;
}
