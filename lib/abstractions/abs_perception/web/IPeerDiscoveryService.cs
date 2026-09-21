namespace JoinCode.Abstractions.Interfaces;

public sealed class PeerInfo {
    /// <summary>获取节点标识。</summary>
    public required string Id { get; init; }
    /// <summary>获取节点名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取连接时间。</summary>
    public required DateTime ConnectedAt { get; init; }
}

public interface IPeerDiscoveryService {
    /// <summary>获取已连接的节点列表。</summary>
    IEnumerable<PeerInfo> GetConnectedPeers();
    event EventHandler<PeerInfo>? PeerConnected;
    event EventHandler<string>? PeerDisconnected;
}