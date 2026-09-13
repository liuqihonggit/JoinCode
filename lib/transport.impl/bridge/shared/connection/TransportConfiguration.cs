namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输配置
/// 注意: 不使用 [Register]，因为需要从 BridgeConfig 初始化，由 Sync 层手动注册
/// </summary>
public class TransportConfiguration : ServiceEntity
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

    /// <summary>首选传输协议，默认 WebSocket</summary>
    public TransportProtocol PreferredProtocol { get; init; } = TransportProtocol.WebSocket;
    /// <summary>WebSocket 端点 URL</summary>
    public string WebSocketEndpoint { get; init; } = DefaultWebSocketEndpoint;
    /// <summary>SSE 端点 URL</summary>
    public string SseEndpoint { get; init; } = DefaultSseEndpoint;
    /// <summary>是否启用自动重连，默认 true</summary>
    public bool AutoReconnect { get; init; } = true;
    /// <summary>最大重连次数，默认 10</summary>
    public int MaxReconnectAttempts { get; init; } = DefaultMaxReconnectAttempts;
    /// <summary>初始重连延迟（毫秒），默认 1000</summary>
    public int ReconnectDelayMs { get; init; } = DefaultReconnectDelayMs;
    /// <summary>最大重连延迟（毫秒），默认 30000</summary>
    public int MaxReconnectDelayMs { get; init; } = DefaultMaxReconnectDelayMs;
    /// <summary>消息去重容量（最近 N 条消息 ID），默认 1000</summary>
    public int MessageDeduplicationCapacity { get; init; } = DefaultMessageDeduplicationCapacity;
}
