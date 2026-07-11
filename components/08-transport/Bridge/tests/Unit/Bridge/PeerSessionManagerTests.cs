
namespace Bridge.Tests;

/// <summary>
/// PeerSessionManager 单元测试
/// 测试对等会话创建、状态转换、事件触发和查询
/// </summary>
public sealed class PeerSessionManagerTests : IAsyncDisposable
{
    private PeerSessionManager _sut = new(NullLogger<PeerSessionManager>.Instance);

    private static PeerSessionManager CreateSut() => new(NullLogger<PeerSessionManager>.Instance);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _sut.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"DisposeAsync timed out during test cleanup: {ex.Message}");
        }
    }

    [Fact]
    public async Task CreatePeerSessionAsync_ShouldCreateSession_WithConnectingStatus()
    {
        // Arrange
        var sut = CreateSut();
        var localPeerId = "peer-local-001";
        var remotePeerId = "peer-remote-001";

        // Act
        var session = await sut.CreatePeerSessionAsync(localPeerId, remotePeerId).ConfigureAwait(true);

        // Assert
        session.Should().NotBeNull();
        session.SessionId.Should().NotBeNullOrEmpty();
        session.LocalPeerId.Should().Be(localPeerId);
        session.RemotePeerId.Should().Be(remotePeerId);
        session.Status.Should().Be(PeerSessionStatus.Connecting);
        session.CreatedAt.Should().BeGreaterThan(0);

        await sut.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task MarkConnectedAsync_ShouldUpdateStatus_ToConnected()
    {
        // Arrange
        var sut = CreateSut();
        var session = await sut.CreatePeerSessionAsync("local-002", "remote-002").ConfigureAwait(true);

        // Act
        await sut.MarkConnectedAsync(session.SessionId).ConfigureAwait(true);

        // Assert
        var retrieved = sut.GetPeerSession(session.SessionId);
        retrieved.Should().NotBeNull();
        retrieved!.Status.Should().Be(PeerSessionStatus.Connected);

        await sut.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ClosePeerSessionAsync_ShouldUpdateStatus_ToDisconnected()
    {
        // Arrange
        var sut = CreateSut();
        var session = await sut.CreatePeerSessionAsync("local-003", "remote-003").ConfigureAwait(true);
        await sut.MarkConnectedAsync(session.SessionId).ConfigureAwait(true);

        // Act
        await sut.ClosePeerSessionAsync(session.SessionId).ConfigureAwait(true);

        // Assert
        // ClosePeerSessionAsync 会从字典中移除会话，因此 GetPeerSession 返回 null
        var retrieved = sut.GetPeerSession(session.SessionId);
        retrieved.Should().BeNull();

        await sut.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task GetPeerSession_ShouldReturnSession_WhenExists()
    {
        // Arrange
        var sut = CreateSut();
        var session = await sut.CreatePeerSessionAsync("local-004", "remote-004").ConfigureAwait(true);

        // Act
        var result = sut.GetPeerSession(session.SessionId);

        // Assert
        result.Should().NotBeNull();
        result!.SessionId.Should().Be(session.SessionId);

        await sut.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task GetPeerSession_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.GetPeerSession("non-existent-session");

        // Assert
        result.Should().BeNull();

        await sut.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task GetActivePeerSessions_ShouldReturnOnlyConnectedSessions()
    {
        // Arrange
        var sut = CreateSut();
        var session1 = await sut.CreatePeerSessionAsync("local-005a", "remote-005a").ConfigureAwait(true);
        var session2 = await sut.CreatePeerSessionAsync("local-005b", "remote-005b").ConfigureAwait(true);

        // 只将 session1 标记为 Connected
        await sut.MarkConnectedAsync(session1.SessionId).ConfigureAwait(true);

        // Act
        var activeSessions = sut.GetActivePeerSessions();

        // Assert
        activeSessions.Should().HaveCount(1);
        activeSessions[0].SessionId.Should().Be(session1.SessionId);

        await sut.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PeerSessionConnected_ShouldFire_WhenMarkedConnected()
    {
        // Arrange
        var sut = CreateSut();
        var session = await sut.CreatePeerSessionAsync("local-006", "remote-006").ConfigureAwait(true);

        PeerSessionEventArgs? capturedArgs = null;
        sut.PeerSessionConnected += (_, args) => capturedArgs = args;

        // Act
        await sut.MarkConnectedAsync(session.SessionId).ConfigureAwait(true);

        // Assert
        capturedArgs.Should().NotBeNull();
        capturedArgs!.Session.SessionId.Should().Be(session.SessionId);
        capturedArgs.Session.Status.Should().Be(PeerSessionStatus.Connected);

        await sut.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PeerSessionDisconnected_ShouldFire_WhenClosed()
    {
        // Arrange
        var sut = CreateSut();
        var session = await sut.CreatePeerSessionAsync("local-007", "remote-007").ConfigureAwait(true);
        await sut.MarkConnectedAsync(session.SessionId).ConfigureAwait(true);

        PeerSessionEventArgs? capturedArgs = null;
        sut.PeerSessionDisconnected += (_, args) => capturedArgs = args;

        // Act
        await sut.ClosePeerSessionAsync(session.SessionId).ConfigureAwait(true);

        // Assert
        capturedArgs.Should().NotBeNull();
        capturedArgs!.Session.SessionId.Should().Be(session.SessionId);
        capturedArgs.Session.Status.Should().Be(PeerSessionStatus.Disconnected);

        await sut.DisposeAsync().ConfigureAwait(true);
    }
}

public sealed class PeerSessionRouterTests
{
    [Fact]
    public void RegisterRoute_ShouldAddRoute()
    {
        // Arrange
        var router = new PeerSessionRouter();

        // Act
        router.RegisterRoute("peer-001", "ws://localhost:8080/peer-001");

        // Assert
        router.HasRoute("peer-001").Should().BeTrue();
        router.GetRoute("peer-001").Should().Be("ws://localhost:8080/peer-001");
        router.RouteCount.Should().Be(1);
    }

    [Fact]
    public void UnregisterRoute_ShouldRemoveRoute()
    {
        // Arrange
        var router = new PeerSessionRouter();
        router.RegisterRoute("peer-002", "ws://localhost:8080/peer-002");

        // Act
        router.UnregisterRoute("peer-002");

        // Assert
        router.HasRoute("peer-002").Should().BeFalse();
        router.GetRoute("peer-002").Should().BeNull();
        router.RouteCount.Should().Be(0);
    }

    [Fact]
    public void GetRoute_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var router = new PeerSessionRouter();

        // Act
        var route = router.GetRoute("non-existent");

        // Assert
        route.Should().BeNull();
    }

    [Fact]
    public void GetAllPeerIds_ShouldReturnAllRegisteredPeerIds()
    {
        // Arrange
        var router = new PeerSessionRouter();
        router.RegisterRoute("peer-003a", "ws://localhost:8080/a");
        router.RegisterRoute("peer-003b", "ws://localhost:8080/b");

        // Act
        var peerIds = router.GetAllPeerIds();

        // Assert
        peerIds.Should().HaveCount(2);
        peerIds.Should().Contain(["peer-003a", "peer-003b"]);
    }

    [Fact]
    public void Clear_ShouldRemoveAllRoutes()
    {
        // Arrange
        var router = new PeerSessionRouter();
        router.RegisterRoute("peer-004a", "ws://localhost:8080/a");
        router.RegisterRoute("peer-004b", "ws://localhost:8080/b");

        // Act
        router.Clear();

        // Assert
        router.RouteCount.Should().Be(0);
    }

    [Fact]
    public void RegisterRoute_ShouldUpdateExistingRoute()
    {
        // Arrange
        var router = new PeerSessionRouter();
        router.RegisterRoute("peer-005", "ws://localhost:8080/old");

        // Act
        router.RegisterRoute("peer-005", "ws://localhost:8080/new");

        // Assert
        router.GetRoute("peer-005").Should().Be("ws://localhost:8080/new");
        router.RouteCount.Should().Be(1);
    }
}
