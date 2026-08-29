namespace IO.Services;

[Register(typeof(IPeerDiscoveryService), ServiceLifetime.Singleton)]
public sealed partial class PeerDiscoveryService : ServiceEntity, IPeerDiscoveryService
{
    private readonly ConcurrentDictionary<string, PeerInfo> _peers = new(StringComparer.Ordinal);
    private readonly ILogger<PeerDiscoveryService>? _logger;

    public event EventHandler<PeerInfo>? PeerConnected;
    public event EventHandler<string>? PeerDisconnected;

    public PeerDiscoveryService(ILogger<PeerDiscoveryService>? logger = null)
    {
        _logger = logger;
    }

    public IEnumerable<PeerInfo> GetConnectedPeers() => _peers.Values;

    public void AddPeer(PeerInfo peer)
    {
        _peers[peer.Id] = peer;
        PeerConnected?.Invoke(this, peer);
        _logger?.LogInformation("Peer connected: {Name} ({Id})", peer.Name, peer.Id);
    }

    public void RemovePeer(string peerId)
    {
        _peers.TryRemove(peerId, out _);
        PeerDisconnected?.Invoke(this, peerId);
        _logger?.LogInformation("Peer disconnected: {Id}", peerId);
    }
}
