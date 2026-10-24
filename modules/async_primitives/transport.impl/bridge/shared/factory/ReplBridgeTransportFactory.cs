namespace JoinCode.Transport.Bridge;

/// <summary>
/// 传输层工厂实现 — 创建 v1/v2 传输实例
/// </summary>
[Register(typeof(IReplBridgeTransportFactory), ServiceLifetime.Singleton)]
public sealed partial class DefaultReplBridgeTransportFactory : ServiceEntity, IReplBridgeTransportFactory {
    /// <inheritdoc />
    public IReplBridgeTransport CreateV1Transport(V1TransportOptions options, ILogger? logger = null) {
        ArgumentNullException.ThrowIfNull(options);
        return V1ReplBridgeTransport.Create(options, logger);
    }

    /// <inheritdoc />
    public IReplBridgeTransport CreateV2Transport(V2TransportOptions options, ILogger? logger = null) {
        ArgumentNullException.ThrowIfNull(options);
        return V2ReplBridgeTransport.Create(options, logger);
    }
}