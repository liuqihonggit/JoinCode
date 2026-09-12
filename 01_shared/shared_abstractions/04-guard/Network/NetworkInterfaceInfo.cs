namespace JoinCode.Abstractions.Network;

/// <summary>
/// 网络接口信息 — 描述单个活跃网络接口(多流场景下一台机器可同时有多个活跃接口)
/// </summary>
public sealed partial class NetworkInterfaceInfo
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public NetworkInterfaceKind Kind { get; init; }
    public bool IsUp { get; init; }
    public IReadOnlyList<string> Addresses { get; init; } = [];
    public long SpeedBitsPerSecond { get; init; }
    public int Mtu { get; init; }
}
