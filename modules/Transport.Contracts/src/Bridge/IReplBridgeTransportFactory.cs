namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输层工厂接口 — 用于创建 v1/v2 传输
/// </summary>
public interface IReplBridgeTransportFactory
{
    /// <summary>
    /// 创建 v2 传输（简化参数，env-less 路径使用）
    /// </summary>
    IReplBridgeTransport CreateV2Transport(string sdkUrl, string sessionId, string workerJwt, int connectTimeoutMs);

    /// <summary>
    /// 创建 v1 传输（完整选项）
    /// </summary>
    IReplBridgeTransport CreateV1Transport(V1TransportOptions options, ILogger? logger = null);

    /// <summary>
    /// 创建 v2 传输（完整选项，v1 CCR 模式下使用）
    /// </summary>
    IReplBridgeTransport CreateV2Transport(V2TransportOptions options, ILogger? logger = null);
}
