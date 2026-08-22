namespace JoinCode.Abstractions.Network;

/// <summary>
/// 网络接口类型 — 用于多流识别(以太网/无线/VPN隧道/环回)
/// </summary>
public enum NetworkInterfaceKind
{
    [EnumValue("ethernet")] Ethernet,
    [EnumValue("wireless")] Wireless,
    [EnumValue("vpn_tunnel")] VpnTunnel,
    [EnumValue("loopback")] Loopback,
    [EnumValue("unknown")] Unknown,
}
