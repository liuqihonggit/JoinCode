namespace JoinCode.Transport.Bridge;

/// <summary>
/// v2 传输适配器选项
/// </summary>
public sealed class V2TransportOptions
{
    /// <summary>SSE 流 URL（/worker/events/stream）</summary>
    public required string SseUrl { get; init; }

    /// <summary>CCR API 基础 URL</summary>
    public required string ApiBaseUrl { get; init; }

    /// <summary>Worker JWT（包含 session_id claim + worker role）</summary>
    public required string IngressToken { get; init; }

    /// <summary>会话 ID</summary>
    public required string SessionId { get; init; }

    /// <summary>Worker epoch（POST /bridge 返回或 registerWorker 获取）</summary>
    public int? Epoch { get; init; }

    /// <summary>初始 SSE 序列号（传输切换时保持位置）</summary>
    public int InitialSequenceNum { get; init; }

    /// <summary>心跳间隔（毫秒），默认 20000</summary>
    public int HeartbeatIntervalMs { get; init; } = 20000;

    /// <summary>心跳抖动比例，默认 0</summary>
    public double HeartbeatJitterFraction { get; init; }

    /// <summary>是否仅出站模式（跳过 SSE 读流）</summary>
    public bool OutboundOnly { get; init; }

    /// <summary>每实例认证头获取闭包（多会话安全）</summary>
    public Func<string?>? GetAuthToken { get; init; }
}
