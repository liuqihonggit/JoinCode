namespace JoinCode.Abstractions.Network;

/// <summary>
/// 网络路由类型 — 当前流量走向(直连/VPN/代理)
/// </summary>
public enum NetworkRouteType
{
    [EnumValue("direct")] Direct,
    [EnumValue("vpn")] Vpn,
    [EnumValue("proxy")] Proxy,
}
