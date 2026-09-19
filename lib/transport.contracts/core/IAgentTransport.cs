namespace JoinCode.Transport;

/// <summary>
/// 传输层事件参数
/// </summary>
public sealed class TransportMessageEventArgs : EventArgs
{
    /// <summary>消息内容</summary>
    public required string Message { get; init; }
    /// <summary>消息来源通道（输出或错误）</summary>
    public required TransportChannel Channel { get; init; }
    /// <summary>消息接收时间戳</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 传输通道类型
/// </summary>
public enum TransportChannel
{
    /// <summary>标准输出通道</summary>
    [EnumValue("output")]
    Output,
    /// <summary>标准错误通道</summary>
    [EnumValue("error")]
    Error,
}

/// <summary>
/// 传输连接状态
/// </summary>
public enum TransportState
{
    /// <summary>已断开</summary>
    [EnumValue("disconnected")] Disconnected,
    /// <summary>连接中</summary>
    [EnumValue("connecting")] Connecting,
    /// <summary>已连接</summary>
    [EnumValue("connected")] Connected,
    /// <summary>连接失败</summary>
    [EnumValue("failed")] Failed
}

/// <summary>
/// Agent 传输层抽象 — 统一 stdin/stdout、SSE、WebSocket、命名管道等通讯方式
/// </summary>
/// <remarks>
/// 使用方式:
/// <code>
/// // 本地子进程
/// var transport = new StdioAgentTransport("jcc.exe", "--mock --trust");
/// // 远程 SSE
/// var transport = new SseAgentTransport("http://host:3456/events/stream", "http://host:3456/messages");
///
/// await transport.ConnectAsync();
/// transport.OnMessage += (s, e) => { ... };
/// await transport.SendMessageAsync("hello");
/// </code>
/// </remarks>
public interface IAgentTransport : IAsyncDisposable
{
    /// <summary>当前连接状态</summary>
    TransportState State { get; }

    /// <summary>传输类型标识（如 "stdio", "sse", "websocket", "pipe"）</summary>
    string TransportType { get; }

    /// <summary>建立连接</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>断开连接</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>发送消息到对端</summary>
    Task SendMessageAsync(string message, CancellationToken ct = default);

    /// <summary>等待输出消息，直到满足条件或超时</summary>
    Task<string> WaitForOutputAsync(Func<string, bool> predicate, TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>等待错误消息，直到满足条件或超时</summary>
    Task<string> WaitForErrorAsync(Func<string, bool> predicate, TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>获取所有已接收的输出</summary>
    Task<string> GetOutputAsync();

    /// <summary>获取自上次调用以来的增量输出 — 避免全量拼接导致大日志超时</summary>
    Task<string> GetOutputIncrementalAsync();

    /// <summary>获取所有已接收的错误</summary>
    Task<string> GetErrorAsync();

    /// <summary>获取自上次调用以来的增量错误 — 避免全量拼接导致大日志超时</summary>
    Task<string> GetErrorIncrementalAsync();

    /// <summary>清空输出缓冲区</summary>
    Task ClearOutputAsync();

    /// <summary>收到消息事件（含 Output 和 Error）</summary>
    event EventHandler<TransportMessageEventArgs>? OnMessage;

    /// <summary>连接状态变更事件</summary>
    event EventHandler<TransportState>? OnStateChanged;
}
