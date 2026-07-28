namespace Core.Bridge;

/// <summary>
/// 桥传输层上下文聚合 — 将 HttpClient + BridgeApiClient 合并为单一参数
/// </summary>
public sealed record BridgeTransportContext
{
    /// <summary>
    /// HTTP 客户端 — 用于 v1 API 调用和 SSE 传输
    /// </summary>
    public required HttpClient HttpClient { get; init; }

    /// <summary>
    /// 桥 API 客户端 — 封装 v1 环境注册/注销/心跳等 API
    /// </summary>
    public required BridgeApiClient ApiClient { get; init; }
}
