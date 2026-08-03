namespace Hands.Tests.Network;

public sealed class PeerDiscoveryServiceTests
{
    private readonly PeerDiscoveryService _service;

    public PeerDiscoveryServiceTests()
    {
        _service = new PeerDiscoveryService();
    }

    [Fact]
    public void GetConnectedPeers_InitiallyEmpty()
    {
        _service.GetConnectedPeers().Should().BeEmpty();
    }

    [Fact]
    public void AddPeer_IncreasesPeerListAndRaisesEvent()
    {
        PeerInfo? raisedPeer = null;
        _service.PeerConnected += (_, peer) => raisedPeer = peer;

        var peer = new PeerInfo { Id = "peer-1", Name = "Test Peer", ConnectedAt = DateTime.UtcNow };
        _service.AddPeer(peer);

        _service.GetConnectedPeers().Should().ContainSingle();
        raisedPeer.Should().Be(peer);
    }

    [Fact]
    public void RemovePeer_RemovesMatchingPeerAndRaisesEvent()
    {
        string? removedId = null;
        _service.PeerDisconnected += (_, id) => removedId = id;
        _service.AddPeer(new PeerInfo { Id = "peer-1", Name = "Peer 1", ConnectedAt = DateTime.UtcNow });
        _service.AddPeer(new PeerInfo { Id = "peer-2", Name = "Peer 2", ConnectedAt = DateTime.UtcNow });

        _service.RemovePeer("peer-1");

        _service.GetConnectedPeers().Should().HaveCount(1);
        _service.GetConnectedPeers().First().Id.Should().Be("peer-2");
        removedId.Should().Be("peer-1");
    }

    [Fact]
    public void GetConnectedPeers_ReturnsSnapshot_DoesNotReflectFutureChanges()
    {
        _service.AddPeer(new PeerInfo { Id = "peer-1", Name = "Peer 1", ConnectedAt = DateTime.UtcNow });

        var snapshot = _service.GetConnectedPeers();
        _service.AddPeer(new PeerInfo { Id = "peer-2", Name = "Peer 2", ConnectedAt = DateTime.UtcNow });

        snapshot.Should().HaveCount(1);
    }
}
