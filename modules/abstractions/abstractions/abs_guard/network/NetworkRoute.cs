namespace JoinCode.Abstractions.Network;

/// <summary>
/// 网络路由信息 — 当前流量的出口路由
/// </summary>
public sealed partial class NetworkRoute {
    /// <summary>获取路由类型。</summary>
    public NetworkRouteType Type { get; init; }
    /// <summary>获取代理 URL。</summary>
    public string? ProxyUrl { get; init; }
    /// <summary>获取出口网络接口。</summary>
    public string? ViaInterface { get; init; }
}
