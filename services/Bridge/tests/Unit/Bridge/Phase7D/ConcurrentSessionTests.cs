
namespace Bridge.Tests.Phase7D;

public sealed class ConcurrentSessionTests
{
    private readonly InMemoryFileSystem _fs = new();

    private ConcurrentSessionService CreateSut() => new(_fs);

    [Fact]
    public async Task RegisterAsync_ShouldWritePidFile()
    {
        var sut = CreateSut();

        var result = await sut.RegisterAsync("test-session-001").ConfigureAwait(true);

        Assert.True(result);
        var path = sut.GetPidFilePath();
        Assert.True(_fs.FileExists(path));

        var json = await _fs.ReadAllTextAsync(path).ConfigureAwait(true);
        var record = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.ConcurrentSessionRecord);
        Assert.NotNull(record);
        Assert.Equal(Environment.ProcessId, record.Pid);
        Assert.Equal("test-session-001", record.SessionId);
        Assert.Equal("interactive", record.Kind);
    }

    [Fact]
    public async Task RegisterAsync_WithDaemonKind_ShouldUseEnvVar()
    {
        var sut = CreateSut();
        Environment.SetEnvironmentVariable("JCC_SESSION_KIND", "daemon");
        try
        {
            await sut.RegisterAsync().ConfigureAwait(true);

            var path = sut.GetPidFilePath();
            var json = await _fs.ReadAllTextAsync(path).ConfigureAwait(true);
            var record = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.ConcurrentSessionRecord);
            Assert.NotNull(record);
            Assert.Equal("daemon", record.Kind);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_SESSION_KIND", null);
        }
    }

    [Fact]
    public async Task UnregisterAsync_ShouldDeletePidFile()
    {
        var sut = CreateSut();
        await sut.RegisterAsync("session-1").ConfigureAwait(true);

        Assert.True(_fs.FileExists(sut.GetPidFilePath()));

        await sut.UnregisterAsync().ConfigureAwait(true);

        Assert.False(_fs.FileExists(sut.GetPidFilePath()));
    }

    [Fact]
    public async Task UnregisterAsync_WhenNoFile_ShouldNotThrow()
    {
        var sut = CreateSut();

        // 不应抛异常
        await sut.UnregisterAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UpdateBridgeSessionIdAsync_ShouldUpdateRecord()
    {
        var sut = CreateSut();
        await sut.RegisterAsync("session-1").ConfigureAwait(true);

        await sut.UpdateBridgeSessionIdAsync("session_compat_123").ConfigureAwait(true);

        var path = sut.GetPidFilePath();
        var json = await _fs.ReadAllTextAsync(path).ConfigureAwait(true);
        var record = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.ConcurrentSessionRecord);
        Assert.NotNull(record);
        Assert.Equal("session_compat_123", record.BridgeSessionId);
        Assert.NotNull(record.UpdatedAt);
    }

    [Fact]
    public async Task UpdateBridgeSessionIdAsync_WithNull_ShouldClearValue()
    {
        var sut = CreateSut();
        await sut.RegisterAsync("session-1").ConfigureAwait(true);
        await sut.UpdateBridgeSessionIdAsync("session_compat_123").ConfigureAwait(true);

        await sut.UpdateBridgeSessionIdAsync(null).ConfigureAwait(true);

        var path = sut.GetPidFilePath();
        var json = await _fs.ReadAllTextAsync(path).ConfigureAwait(true);
        var record = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.ConcurrentSessionRecord);
        Assert.NotNull(record);
        Assert.Null(record.BridgeSessionId);
    }

    [Fact]
    public async Task UpdateSessionNameAsync_ShouldUpdateName()
    {
        var sut = CreateSut();
        await sut.RegisterAsync("session-1").ConfigureAwait(true);

        await sut.UpdateSessionNameAsync("my-session").ConfigureAwait(true);

        var path = sut.GetPidFilePath();
        var json = await _fs.ReadAllTextAsync(path).ConfigureAwait(true);
        var record = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.ConcurrentSessionRecord);
        Assert.NotNull(record);
        Assert.Equal("my-session", record.Name);
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_ShouldUpdateStatus()
    {
        var sut = CreateSut();
        await sut.RegisterAsync("session-1").ConfigureAwait(true);

        await sut.UpdateSessionActivityAsync("busy", "user_input").ConfigureAwait(true);

        var path = sut.GetPidFilePath();
        var json = await _fs.ReadAllTextAsync(path).ConfigureAwait(true);
        var record = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.ConcurrentSessionRecord);
        Assert.NotNull(record);
        Assert.Equal("busy", record.Status);
        Assert.Equal("user_input", record.WaitingFor);
    }

    [Fact]
    public async Task UpdateAsync_WhenNoPidFile_ShouldNotThrow()
    {
        var sut = CreateSut();

        // PID 文件不存在，不应抛异常
        await sut.UpdateBridgeSessionIdAsync("test").ConfigureAwait(true);
    }

    [Fact]
    public void CountConcurrentSessions_WhenNoDir_ShouldReturn0()
    {
        var sut = CreateSut();

        var count = sut.CountConcurrentSessions();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CountConcurrentSessions_ShouldCountCurrentProcess()
    {
        var sut = CreateSut();
        await sut.RegisterAsync("session-1").ConfigureAwait(true);

        var count = sut.CountConcurrentSessions();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CountConcurrentSessions_ShouldCleanupStaleFiles()
    {
        var sut = CreateSut();
        await sut.RegisterAsync("session-1").ConfigureAwait(true);

        // 写入一个过期 PID 文件（PID 不存在）
        var sessionsDir = ConcurrentSessionService.GetSessionsDir();
        if (!_fs.DirectoryExists(sessionsDir))
        {
            _fs.CreateDirectory(sessionsDir);
        }
        var stalePath = Path.Combine(sessionsDir, "99999999.json");
        var staleRecord = new ConcurrentSessionRecord
        {
            Pid = 99999999,
            SessionId = "stale",
            StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Kind = "interactive",
        };
        var staleJson = JsonSerializer.Serialize(staleRecord, BridgeJsonContext.Default.ConcurrentSessionRecord);
        await _fs.WriteAllTextAsync(stalePath, staleJson).ConfigureAwait(true);

        var count = sut.CountConcurrentSessions();

        // 当前进程 1 + 过期文件被清理
        Assert.Equal(1, count);
        Assert.False(_fs.FileExists(stalePath));
    }

    [Fact]
    public async Task RegisterAsync_ShouldCreateSessionsDir()
    {
        var sut = CreateSut();
        var sessionsDir = ConcurrentSessionService.GetSessionsDir();

        // 目录可能不存在
        var result = await sut.RegisterAsync("session-1").ConfigureAwait(true);

        Assert.True(result);
        Assert.True(_fs.DirectoryExists(sessionsDir));
    }
}
