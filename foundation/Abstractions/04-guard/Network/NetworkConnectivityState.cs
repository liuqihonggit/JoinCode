namespace JoinCode.Abstractions.Network;

/// <summary>
/// 网络连接状态 — 系统级网络可用性的高层抽象
/// </summary>
public enum NetworkConnectivityState
{
    [EnumValue("offline")] Offline,
    [EnumValue("online")] Online,
    [EnumValue("online_with_vpn")] OnlineWithVpn,
    [EnumValue("online_with_proxy")] OnlineWithProxy,
}
