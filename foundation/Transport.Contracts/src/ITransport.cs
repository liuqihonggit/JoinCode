namespace JoinCode.Transport;

/// <summary>
/// 消息无关的通用传输接口 — 不依赖任何上层协议类型
/// </summary>
/// <remarks>
/// 用 byte[] 载荷替代 JsonRpcMessage/string，使传输层完全不感知上层协议。
/// MCP 传输层可在此接口上构建 JSON-RPC 协议适配。
/// </remarks>
public interface ITransport : IAsyncDisposable
{
    /// <summary>建立传输连接</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>断开传输连接</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>发送原始字节载荷</summary>
    Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default);

    /// <summary>是否正在运行</summary>
    bool IsRunning { get; }

    /// <summary>收到字节载荷消息</summary>
    event EventHandler<TransportPayloadEventArgs>? PayloadReceived;

    /// <summary>传输错误</summary>
    event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

    /// <summary>连接已关闭</summary>
    event EventHandler? ConnectionClosed;
}

/// <summary>
/// 字节载荷消息事件参数
/// </summary>
public sealed class TransportPayloadEventArgs(ReadOnlyMemory<byte> payload) : EventArgs
{
    /// <summary>原始字节载荷</summary>
    public ReadOnlyMemory<byte> Payload { get; } = payload;
}

/// <summary>
/// 传输错误事件参数
/// </summary>
public sealed class TransportErrorEventArgs : EventArgs
{
    /// <summary>错误异常</summary>
    public Exception Exception { get; }

    /// <summary>错误描述消息</summary>
    public string? Message { get; }

    public TransportErrorEventArgs(Exception exception)
    {
        Exception = exception;
    }

    public TransportErrorEventArgs(Exception exception, string message)
    {
        Exception = exception;
        Message = message;
    }
}
