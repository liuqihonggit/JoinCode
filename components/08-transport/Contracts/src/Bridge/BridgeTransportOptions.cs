namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输层版本 — 对齐 TS 端 v1/v2 选择
/// </summary>
public enum BridgeTransportVersion
{
    /// <summary>v1: HybridTransport（WS 读 + HTTP POST 写到 Session-Ingress）</summary>
    [EnumValue("v1")] V1,

    /// <summary>v2: SSETransport（读）+ CCRClient（写到 CCR v2 /worker/*）</summary>
    [EnumValue("v2")] V2
}

/// <summary>
/// v1 传输适配器选项
/// </summary>
public sealed class V1TransportOptions
{
    /// <summary>WebSocket 端点 URL（Session-Ingress WS）</summary>
    public required string WebSocketEndpoint { get; init; }

    /// <summary>HTTP POST 端点 URL（Session-Ingress /session/{id}/events）</summary>
    public required string PostEndpoint { get; init; }

    /// <summary>OAuth 认证头</summary>
    public string? AuthHeader { get; init; }

    /// <summary>
    /// 刷新认证头回调 — 对齐 TS 端 WebSocketTransport.refreshHeaders
    /// WS 重连时调用以获取最新 OAuth Token
    /// </summary>
    public Func<string?>? RefreshHeaders { get; init; }

    /// <summary>最大连续写入失败次数（超过后丢弃批次）— 对齐 TS 端 maxConsecutiveFailures，默认 50</summary>
    public int MaxConsecutiveFailures { get; init; } = 50;
}

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
