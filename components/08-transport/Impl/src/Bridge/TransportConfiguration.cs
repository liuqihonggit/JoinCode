namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输配置
/// 注意: 不使用 [Register]，因为需要从 BridgeConfig 初始化，由 Sync 层手动注册
/// </summary>
public class TransportConfiguration
{
    /// <summary>默认 WebSocket 端点</summary>
    public const string DefaultWebSocketEndpoint = "ws://localhost:3456/bridge";
    /// <summary>默认 SSE 端点</summary>
    public const string DefaultSseEndpoint = "http://localhost:3456/sse";
    /// <summary>默认最大重连次数</summary>
    public const int DefaultMaxReconnectAttempts = 10;
    /// <summary>默认重连延迟（毫秒）</summary>
    public const int DefaultReconnectDelayMs = 1000;
    /// <summary>默认最大重连延迟（毫秒）</summary>
    public const int DefaultMaxReconnectDelayMs = 30000;
    /// <summary>默认消息去重容量</summary>
    public const int DefaultMessageDeduplicationCapacity = 1000;
    /// <summary>默认缓冲区大小（字节）</summary>
    public const int DefaultBufferSizeBytes = 8192;
    /// <summary>默认重试延迟（毫秒）</summary>
    public const int DefaultRetryDelayMs = 1000;

    public TransportProtocol PreferredProtocol { get; init; } = TransportProtocol.WebSocket;
    public string WebSocketEndpoint { get; init; } = DefaultWebSocketEndpoint;
    public string SseEndpoint { get; init; } = DefaultSseEndpoint;
    public bool AutoReconnect { get; init; } = true;
    public int MaxReconnectAttempts { get; init; } = DefaultMaxReconnectAttempts;
    public int ReconnectDelayMs { get; init; } = DefaultReconnectDelayMs;
    public int MaxReconnectDelayMs { get; init; } = DefaultMaxReconnectDelayMs;
    public int MessageDeduplicationCapacity { get; init; } = DefaultMessageDeduplicationCapacity;
}
