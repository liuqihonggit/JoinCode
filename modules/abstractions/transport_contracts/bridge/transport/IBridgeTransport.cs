namespace JoinCode.Transport.Bridge;

/// <summary>
/// Bridge 传输层接口 — 抽象 Bridge 消息的底层传输机制
/// </summary>
public interface IBridgeTransport {
    /// <summary>启动传输层</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>停止传输层</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息到对端
    /// </summary>
    /// <param name="message">要发送的消息字符串</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SendAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>收到消息事件</summary>
    event EventHandler<TransportMessageReceivedEventArgs>? MessageReceived;

    /// <summary>发生错误事件</summary>
    event EventHandler<TransportErrorEventArgs>? ErrorOccurred;
}

/// <summary>
/// 传输消息接收事件参数
/// </summary>
public sealed class TransportMessageReceivedEventArgs(string message) : EventArgs {
    /// <summary>接收到的消息字符串</summary>
    public string Message { get; } = message;
}