namespace JoinCode.Abstractions.Network;

/// <summary>
/// 网络接口信息 — 描述单个活跃网络接口(多流场景下一台机器可同时有多个活跃接口)
/// </summary>
public sealed partial class NetworkInterfaceInfo {
    /// <summary>获取接口名称。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>获取接口描述。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>获取接口类型。</summary>
    public NetworkInterfaceKind Kind { get; init; }
    /// <summary>获取一个值，指示接口是否启用。</summary>
    public bool IsUp { get; init; }
    /// <summary>获取地址列表。</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];
    /// <summary>获取速度（比特/秒）。</summary>
    public long SpeedBitsPerSecond { get; init; }
    /// <summary>获取 MTU 值。</summary>
    public int Mtu { get; init; }
}