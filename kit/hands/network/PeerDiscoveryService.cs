namespace IO.Services;

/// <summary>对等端发现服务 — 维护已连接对等端列表，提供添加、移除与查询能力，并在变更时触发事件通知。</summary>
[Register(typeof(IPeerDiscoveryService), ServiceLifetime.Singleton)]
public sealed partial class PeerDiscoveryService : ServiceEntity, IPeerDiscoveryService {
    private ImmutableDictionary<string, PeerInfo> _peers = ImmutableDictionary<string, PeerInfo>.Empty.WithComparers(StringComparer.Ordinal);
    private readonly ILogger<PeerDiscoveryService>? _logger;

    /// <summary>当有新对等端加入时触发，参数为加入的对等端信息。</summary>
    public event EventHandler<PeerInfo>? PeerConnected;
    /// <summary>当有对等端断开时触发，参数为断开的对等端标识。</summary>
    public event EventHandler<string>? PeerDisconnected;

    /// <summary>构造对等端发现服务实例。</summary>
    /// <param name="logger">可选的日志记录器，传入 null 时静默运行。</param>
    public PeerDiscoveryService(ILogger<PeerDiscoveryService>? logger = null) {
        _logger = logger;
    }

    /// <summary>获取当前已连接的所有对等端快照拷贝。</summary>
    /// <returns>当前已连接对等端的数组快照。</returns>
    public PeerInfo[] GetConnectedPeers() => Volatile.Read(ref _peers).Values.ToArray();

    /// <summary>添加一个对等端到已连接集合，并触发 <see cref="PeerConnected"/> 事件。</summary>
    /// <param name="peer">要添加的对等端信息。</param>
    public void AddPeer(PeerInfo peer) {
        ImmutableInterlocked.Update(ref _peers, d => d.SetItem(peer.Id, peer));
        PeerConnected?.Invoke(this, peer);
        _logger?.LogInformation("Peer connected: {Name} ({Id})", peer.Name, peer.Id);
    }

    /// <summary>按标识移除已连接的对等端，并触发 <see cref="PeerDisconnected"/> 事件。</summary>
    /// <param name="peerId">要移除的对等端标识。</param>
    public void RemovePeer(string peerId) {
        ImmutableInterlocked.Update(ref _peers, d => d.Remove(peerId));
        PeerDisconnected?.Invoke(this, peerId);
        _logger?.LogInformation("Peer disconnected: {Id}", peerId);
    }
}