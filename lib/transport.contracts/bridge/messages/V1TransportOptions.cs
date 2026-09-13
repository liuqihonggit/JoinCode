namespace JoinCode.Transport.Bridge;

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
