namespace JoinCode.Abstractions.Network;

/// <summary>
/// 网络路由信息 — 当前流量的出口路由
/// </summary>
public sealed partial class NetworkRoute
{
    public NetworkRouteType Type { get; init; }
    public string? ProxyUrl { get; init; }
    public string? ViaInterface { get; init; }
}
