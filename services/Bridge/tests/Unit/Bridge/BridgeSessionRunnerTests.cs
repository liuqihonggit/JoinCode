
namespace Bridge.Tests;

/// <summary>
/// BridgeSessionRunner 单元测试
/// 测试会话创建、停止、查询、Keep-Alive 和过期清理
/// </summary>
public sealed class BridgeSessionRunnerTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _fakeTime;
    private readonly BridgeSessionFactory _sessionFactory;
    private BridgeSessionRunner _sut;

    public BridgeSessionRunnerTests()
    {
        _fakeTime = new FakeTimeProvider();
        _sessionFactory = new BridgeSessionFactory(_fakeTime);
        _sut = CreateSut();
    }

    private BridgeSessionRunner CreateSut(BridgeSessionConfiguration? config = null) =>
        new(_sessionFactory, config, NullLogger.Instance, _fakeTime);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _sut.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"DisposeAsync timed out during test cleanup: {ex.Message}");
        }
    }

    [Fact]
    public async Task StartSessionAsync_ShouldCreateActiveSession()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { ["env"] = "test" };

        // Act
        var session = await _sut.StartSessionAsync("client-001", metadata).ConfigureAwait(true);

        // Assert
        session.Should().NotBeNull();
        session.SessionId.Should().NotBeNullOrEmpty();
        session.ClientId.Should().Be("client-001");
        session.Status.Should().Be(BridgeSessionStatus.Active);
        session.CreatedAt.Should().BeCloseTo(_fakeTime.GetUtcNow(), TimeSpan.FromSeconds(5));
        session.LastActiveAt.Should().BeCloseTo(_fakeTime.GetUtcNow(), TimeSpan.FromSeconds(5));
        session.Metadata.Should().NotBeNull();
        session.Metadata!["env"].Should().Be("test");
    }

    [Fact]
    public async Task StartSessionAsync_ShouldThrow_WhenMaxSessionsReached()
    {
        // Arrange
        var config = new BridgeSessionConfiguration { MaxActiveSessions = 1 };
        try
        {
            await _sut.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"DisposeAsync timed out before recreating SUT with new config: {ex.Message}");
        }
        _sut = CreateSut(config);

        await _sut.StartSessionAsync("client-001").ConfigureAwait(true);

        // Act
        var act = async () => await _sut.StartSessionAsync("client-002").ConfigureAwait(true);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*上限*").ConfigureAwait(true);
    }

    [Fact]
    public async Task StopSessionAsync_ShouldCloseSession()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-003").ConfigureAwait(true);

        // Act
        await _sut.StopSessionAsync(session.SessionId).ConfigureAwait(true);

        // Assert
        var found = _sut.GetSession(session.SessionId);
        found.Should().NotBeNull();
        found!.Status.Should().Be(BridgeSessionStatus.Closed);
    }

    [Fact]
    public async Task GetSession_ShouldReturnSession_WhenExists()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-004").ConfigureAwait(true);

        // Act
        var found = _sut.GetSession(session.SessionId);

        // Assert
        found.Should().NotBeNull();
        found!.SessionId.Should().Be(session.SessionId);
        found.ClientId.Should().Be("client-004");
    }

    [Fact]
    public async Task GetSession_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var fakeId = Guid.NewGuid().ToString("N");

        // Act
        var found = _sut.GetSession(fakeId);

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveSessions_ShouldReturnOnlyActiveSessions()
    {
        // Arrange
        var session1 = await _sut.StartSessionAsync("client-005").ConfigureAwait(true);
        var session2 = await _sut.StartSessionAsync("client-006").ConfigureAwait(true);
        await _sut.StopSessionAsync(session1.SessionId).ConfigureAwait(true);

        // Act
        var activeSessions = _sut.GetActiveSessions();

        // Assert
        activeSessions.Should().HaveCount(1);
        activeSessions[0].SessionId.Should().Be(session2.SessionId);
        activeSessions[0].Status.Should().Be(BridgeSessionStatus.Active);
    }

    [Fact]
    public async Task KeepAliveAsync_ShouldUpdateLastActiveAt()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-007").ConfigureAwait(true);
        var originalLastActiveAt = session.LastActiveAt;

        // 模拟时间流逝
        _fakeTime.Advance(TimeSpan.FromMilliseconds(50));

        // Act
        var result = await _sut.KeepAliveAsync(session.SessionId).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
        session.LastActiveAt.Should().BeAfter(originalLastActiveAt);
    }

    [Fact]
    public async Task CleanupExpiredSessionsAsync_ShouldRemoveExpiredSessions()
    {
        // Arrange - 使用极短超时以便快速过期
        var config = new BridgeSessionConfiguration
        {
            SessionTimeout = TimeSpan.FromMilliseconds(100),
            CleanupInterval = TimeSpan.FromMinutes(5), // 后台循环不影响手动清理
            MaxActiveSessions = 100
        };
        try
        {
            await _sut.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"DisposeAsync timed out before recreating SUT with cleanup config: {ex.Message}");
        }
        _sut = CreateSut(config);

        var session = await _sut.StartSessionAsync("client-008").ConfigureAwait(true);

        // 推进时间使会话超时
        _fakeTime.Advance(TimeSpan.FromMilliseconds(200));

        // Act
        var cleanedCount = await _sut.CleanupExpiredSessionsAsync().ConfigureAwait(true);

        // Assert
        cleanedCount.Should().BeGreaterThanOrEqualTo(1);
        _sut.GetSession(session.SessionId).Should().BeNull("过期会话应被移除");
    }

    [Fact]
    public async Task SuspendSessionAsync_ShouldSetStatusToSuspended()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-suspend-001").ConfigureAwait(true);

        // Act
        await _sut.SuspendSessionAsync(session.SessionId).ConfigureAwait(true);

        // Assert
        var found = _sut.GetSession(session.SessionId);
        found.Should().NotBeNull();
        found!.Status.Should().Be(BridgeSessionStatus.Suspended);
    }

    [Fact]
    public async Task SuspendSessionAsync_ShouldNotSuspend_ClosedSession()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-suspend-002").ConfigureAwait(true);
        await _sut.StopSessionAsync(session.SessionId).ConfigureAwait(true);

        // Act - 挂起已关闭的会话应被忽略
        await _sut.SuspendSessionAsync(session.SessionId).ConfigureAwait(true);

        // Assert - 状态仍为 Closed
        var found = _sut.GetSession(session.SessionId);
        found.Should().NotBeNull();
        found!.Status.Should().Be(BridgeSessionStatus.Closed);
    }

    [Fact]
    public async Task ResumeSessionAsync_ShouldSetStatusToActive()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-resume-001").ConfigureAwait(true);
        await _sut.SuspendSessionAsync(session.SessionId).ConfigureAwait(true);

        // Act
        await _sut.ResumeSessionAsync(session.SessionId).ConfigureAwait(true);

        // Assert
        var found = _sut.GetSession(session.SessionId);
        found.Should().NotBeNull();
        found!.Status.Should().Be(BridgeSessionStatus.Active);
    }

    [Fact]
    public async Task ResumeSessionAsync_ShouldNotResume_NonSuspendedSession()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-resume-002").ConfigureAwait(true);

        // Act - 恢复非挂起会话应被忽略
        await _sut.ResumeSessionAsync(session.SessionId).ConfigureAwait(true);

        // Assert - 状态仍为 Active
        var found = _sut.GetSession(session.SessionId);
        found.Should().NotBeNull();
        found!.Status.Should().Be(BridgeSessionStatus.Active);
    }

    [Fact]
    public async Task CreateSnapshot_ShouldCaptureSessionState()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-snapshot-001").ConfigureAwait(true);

        // Act
        var snapshot = _sut.CreateSnapshot(session.SessionId);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.SessionId.Should().Be(session.SessionId);
        snapshot.ClientId.Should().Be("client-snapshot-001");
        snapshot.State.Should().Be(BridgeSessionStatus.Active);
        snapshot.CapturedAt.Should().BeCloseTo(_fakeTime.GetUtcNow(), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateSnapshot_ShouldReturnNull_WhenSessionNotFound()
    {
        // Act
        var snapshot = _sut.CreateSnapshot("non-existent-session");

        // Assert
        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task RestoreFromSnapshotAsync_ShouldResumeSuspendedSession()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-restore-001").ConfigureAwait(true);
        var snapshot = _sut.CreateSnapshot(session.SessionId);
        await _sut.SuspendSessionAsync(session.SessionId).ConfigureAwait(true);

        // Act - 快照记录为 Active，当前为 Suspended，应恢复
        var restored = await _sut.RestoreFromSnapshotAsync(snapshot!).ConfigureAwait(true);

        // Assert
        restored.Should().NotBeNull();
        restored!.Status.Should().Be(BridgeSessionStatus.Active);
    }

    [Fact]
    public async Task RestoreFromSnapshotAsync_ShouldReturnNull_WhenSessionClosed()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-restore-002").ConfigureAwait(true);
        var snapshot = _sut.CreateSnapshot(session.SessionId);
        await _sut.StopSessionAsync(session.SessionId).ConfigureAwait(true);

        // Act
        var restored = await _sut.RestoreFromSnapshotAsync(snapshot!).ConfigureAwait(true);

        // Assert
        restored.Should().BeNull();
    }

    [Fact]
    public async Task SessionStateChanged_ShouldFire_OnSuspendAndResume()
    {
        // Arrange
        var session = await _sut.StartSessionAsync("client-events-001").ConfigureAwait(true);
        var stateChanges = new List<BridgeSessionStateChangedEventArgs>();
        _sut.SessionStateChanged += (_, args) => stateChanges.Add(args);

        // Act
        await _sut.SuspendSessionAsync(session.SessionId).ConfigureAwait(true);
        await _sut.ResumeSessionAsync(session.SessionId).ConfigureAwait(true);

        // Assert
        stateChanges.Should().HaveCount(2);
        stateChanges[0].NewStatus.Should().Be(BridgeSessionStatus.Suspended);
        stateChanges[0].PreviousStatus.Should().Be(BridgeSessionStatus.Active);
        stateChanges[1].NewStatus.Should().Be(BridgeSessionStatus.Active);
        stateChanges[1].PreviousStatus.Should().Be(BridgeSessionStatus.Suspended);
    }
}
