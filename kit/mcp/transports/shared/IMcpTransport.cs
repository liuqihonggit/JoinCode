
namespace McpClient.Transports;

/// <summary>
/// MCP 传输接口 — 抽象 JSON-RPC 消息的底层传输通道(stdio/HTTP/WebSocket 等),
/// 统一启动/停止/发送/接收生命周期。
/// </summary>
public interface IMcpTransport : IAsyncDisposable
{
    /// <summary>
    /// 启动传输连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步启动操作的任务</returns>
    Task StartAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// 停止传输连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步停止操作的任务</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// 发送 JSON-RPC 消息
    /// </summary>
    /// <param name="message">JSON-RPC 消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步发送操作的任务</returns>
    Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default);
    /// <summary>收到 JSON-RPC 消息事件</summary>
    event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;
    /// <summary>传输错误事件</summary>
    event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;
    /// <summary>当前是否运行中</summary>
    bool IsRunning { get; }
}

/// <summary>
/// 消息接收事件参数 — 携带收到的 JSON-RPC 消息与连接标识
/// </summary>
public class McpMessageReceivedEventArgs : EventArgs
{
    /// <summary>收到的 JSON-RPC 消息</summary>
    public required JsonRpcMessage Message { get; init; }
    /// <summary>连接标识,可为 null</summary>
    public string? ConnectionId { get; init; }
}

/// <summary>
/// 传输错误事件参数 — 携带异常与连接标识
/// </summary>
public class McpTransportErrorEventArgs : EventArgs
{
    /// <summary>传输异常</summary>
    public required Exception Exception { get; init; }
    /// <summary>连接标识,可为 null</summary>
    public string? ConnectionId { get; init; }
}
