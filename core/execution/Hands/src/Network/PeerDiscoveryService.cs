using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class PeerDiscoveryService : IPeerDiscoveryService
{
    private readonly Dictionary<string, PeerInfo> _peers = new(StringComparer.Ordinal);
    [Inject] private readonly ILogger<PeerDiscoveryService>? _logger;

    public event EventHandler<PeerInfo>? PeerConnected;
    public event EventHandler<string>? PeerDisconnected;

    public PeerDiscoveryService(ILogger<PeerDiscoveryService>? logger = null)
    {
        _logger = logger;
    }

    public IEnumerable<PeerInfo> GetConnectedPeers()
    {
        lock (_peers)
        {
            return _peers.Values;
        }
    }

    public void AddPeer(PeerInfo peer)
    {
        lock (_peers)
        {
            _peers[peer.Id] = peer;
        }
        PeerConnected?.Invoke(this, peer);
        _logger?.LogInformation("Peer connected: {Name} ({Id})", peer.Name, peer.Id);
    }

    public void RemovePeer(string peerId)
    {
        lock (_peers)
        {
            _peers.Remove(peerId);
        }
        PeerDisconnected?.Invoke(this, peerId);
        _logger?.LogInformation("Peer disconnected: {Id}", peerId);
    }
}
