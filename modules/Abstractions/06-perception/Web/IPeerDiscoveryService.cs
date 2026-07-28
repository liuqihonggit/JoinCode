namespace JoinCode.Abstractions.Interfaces;

public sealed class PeerInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DateTime ConnectedAt { get; init; }
}

public interface IPeerDiscoveryService
{
    IReadOnlyList<PeerInfo> GetConnectedPeers();
    event EventHandler<PeerInfo>? PeerConnected;
    event EventHandler<string>? PeerDisconnected;
}
