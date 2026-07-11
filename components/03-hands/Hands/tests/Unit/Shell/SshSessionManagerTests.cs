
namespace Core.Tests.Ssh;

public sealed class SshSessionManagerTests
{
    private readonly SshSessionManager _sut;

    public SshSessionManagerTests()
    {
        _sut = new SshSessionManager(TestFileSystem.Current, NullLogger<SshSessionManager>.Instance);
    }

    [Fact]
    public async Task CreateSessionAsync_WithValidConfig_ReturnsSession()
    {
        var config = new SshSessionConfig
        {
            Host = "example.com",
            Username = "user"
        };

        var session = await _sut.CreateSessionAsync(config).ConfigureAwait(true);

        session.Should().NotBeNull();
        session.SessionId.Should().NotBeNullOrEmpty();
        session.Config.Should().BeSameAs(config);
        session.ConnectionState.Should().Be(SshConnectionState.Disconnected);
    }

    [Fact]
    public async Task CreateSessionAsync_WithNullConfig_ThrowsArgumentNullException()
    {
        var act = () => _sut.CreateSessionAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task CreateSessionAsync_GeneratesUniqueSessionIds()
    {
        var config = new SshSessionConfig
        {
            Host = "example.com",
            Username = "user"
        };

        var session1 = await _sut.CreateSessionAsync(config).ConfigureAwait(true);
        var session2 = await _sut.CreateSessionAsync(config).ConfigureAwait(true);

        session1.SessionId.Should().NotBe(session2.SessionId);
    }

    [Fact]
    public async Task GetSession_WithExistingId_ReturnsSession()
    {
        var config = new SshSessionConfig
        {
            Host = "example.com",
            Username = "user"
        };

        var session = await _sut.CreateSessionAsync(config).ConfigureAwait(true);
        var retrieved = _sut.GetSession(session.SessionId);

        retrieved.Should().NotBeNull();
        retrieved!.SessionId.Should().Be(session.SessionId);
    }

    [Fact]
    public void GetSession_WithNonExistentId_ReturnsNull()
    {
        var result = _sut.GetSession("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveSessions_WithNoConnectedSessions_ReturnsEmpty()
    {
        var config = new SshSessionConfig
        {
            Host = "example.com",
            Username = "user"
        };

        await _sut.CreateSessionAsync(config).ConfigureAwait(true);

        var active = _sut.GetActiveSessions();

        active.Should().BeEmpty();
    }

    [Fact]
    public async Task DestroySessionAsync_RemovesSession()
    {
        var config = new SshSessionConfig
        {
            Host = "example.com",
            Username = "user"
        };

        var session = await _sut.CreateSessionAsync(config).ConfigureAwait(true);
        await _sut.DestroySessionAsync(session.SessionId).ConfigureAwait(true);

        _sut.GetSession(session.SessionId).Should().BeNull();
    }

    [Fact]
    public async Task DestroySessionAsync_WithNonExistentId_DoesNotThrow()
    {
        var act = () => _sut.DestroySessionAsync("nonexistent");

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SessionStateChanged_FiredOnCreateAndDestroy()
    {
        var events = new List<SshSessionStateChangedEventArgs>();
        _sut.SessionStateChanged += (_, e) => events.Add(e);

        var config = new SshSessionConfig
        {
            Host = "example.com",
            Username = "user"
        };

        var session = await _sut.CreateSessionAsync(config).ConfigureAwait(true);
        await _sut.DestroySessionAsync(session.SessionId).ConfigureAwait(true);

        events.Should().HaveCountGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task DisposeAsync_CleansUpAllSessions()
    {
        var config = new SshSessionConfig
        {
            Host = "example.com",
            Username = "user"
        };

        await _sut.CreateSessionAsync(config).ConfigureAwait(true);
        await _sut.CreateSessionAsync(new SshSessionConfig
        {
            Host = "other.com",
            Username = "user"
        }).ConfigureAwait(true);

        await _sut.DisposeAsync().ConfigureAwait(true);

        _sut.GetActiveSessions().Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        await _sut.DisposeAsync().ConfigureAwait(true);
        await _sut.DisposeAsync().ConfigureAwait(true);
    }
}
