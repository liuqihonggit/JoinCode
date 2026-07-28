
namespace Core.Bridge;

// BridgeTransportVersion 已迁移到 JoinCode.Transport 命名空间 (Transport.Contracts)

/// <summary>
/// 传输层工厂 — 对齐 TS 端 createV1ReplTransport / createV2ReplTransport
/// 将 v1/v2 选择封装在构造站点，调用方只依赖 IReplBridgeTransport
/// </summary>
public static class ReplBridgeTransportFactory
{
    /// <summary>
    /// 创建 v1 传输 — 对齐 TS 端 createV1ReplTransport
    /// </summary>
    public static IReplBridgeTransport CreateV1Transport(V1TransportOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new JoinCode.Transport.Bridge.V1ReplBridgeTransport(options, logger);
    }

    /// <summary>
    /// 创建 v2 传输 — 对齐 TS 端 createV2ReplTransport
    /// </summary>
    public static IReplBridgeTransport CreateV2Transport(V2TransportOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new JoinCode.Transport.Bridge.V2ReplBridgeTransport(options, logger);
    }

    /// <summary>
    /// 根据版本创建传输
    /// </summary>
    public static IReplBridgeTransport Create(
        BridgeTransportVersion version,
        V1TransportOptions? v1Options = null,
        V2TransportOptions? v2Options = null,
        ILogger? logger = null)
    {
        return version switch
        {
            BridgeTransportVersion.V1 when v1Options is not null
                => CreateV1Transport(v1Options, logger),
            BridgeTransportVersion.V2 when v2Options is not null
                => CreateV2Transport(v2Options, logger),
            _ => throw new ArgumentException($"传输版本 {version} 需要对应的选项参数")
        };
    }
}
