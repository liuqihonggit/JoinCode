namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输层工厂实现 — 创建 v1/v2 传输实例
/// </summary>
[Register]
public sealed partial class DefaultReplBridgeTransportFactory : IReplBridgeTransportFactory
{
    /// <inheritdoc />
    public IReplBridgeTransport CreateV2Transport(string sdkUrl, string sessionId, string workerJwt, int connectTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(sdkUrl);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(workerJwt);

        var options = new V2TransportOptions
        {
            SseUrl = $"{sdkUrl}/worker/events/stream",
            ApiBaseUrl = sdkUrl,
            IngressToken = workerJwt,
            SessionId = sessionId,
            HeartbeatIntervalMs = connectTimeoutMs,
        };

        return new V2ReplBridgeTransport(options);
    }

    /// <inheritdoc />
    public IReplBridgeTransport CreateV1Transport(V1TransportOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new V1ReplBridgeTransport(options, logger);
    }

    /// <inheritdoc />
    public IReplBridgeTransport CreateV2Transport(V2TransportOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new V2ReplBridgeTransport(options, logger);
    }
}
