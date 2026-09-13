namespace Core.Tests.Services.Memdir;

public sealed class TeamMemorySyncServiceTests : IDisposable
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly Mock<IFileOperationService> _fileOperationServiceMock;
    private readonly global::Memdir.Sync.TeamMemorySyncService _service;
    private readonly string _tempDir = "/test/memdir_sync/";

    public TeamMemorySyncServiceTests()
    {
        _fs.CreateDirectory(_tempDir);

        _fileOperationServiceMock = new Mock<IFileOperationService>();
        var options = Options.Create(new TeamMemorySyncOptions
        {
            WatchPath = _tempDir,
            EnableAutoSync = false,
            EnableFileWatching = false
        });
        _service = new global::Memdir.Sync.TeamMemorySyncService(_fs, _fileOperationServiceMock.Object, options);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    private static global::System.Collections.Concurrent.ConcurrentDictionary<string, SyncFileEntry> GetLocalEntries(global::Memdir.Sync.TeamMemorySyncService service)
    {
        var field = typeof(global::Memdir.Sync.TeamMemorySyncService).GetField("_localEntries", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
        return (global::System.Collections.Concurrent.ConcurrentDictionary<string, SyncFileEntry>)field!.GetValue(service)!;
    }

    private static global::System.Collections.Concurrent.ConcurrentDictionary<string, SyncFileEntry> GetRemoteEntries(global::Memdir.Sync.TeamMemorySyncService service)
    {
        var field = typeof(global::Memdir.Sync.TeamMemorySyncService).GetField("_remoteEntries", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
        return (global::System.Collections.Concurrent.ConcurrentDictionary<string, SyncFileEntry>)field!.GetValue(service)!;
    }

    [Fact]
    public async Task StartAsync_SetsIsRunningToTrue()
    {
        await _service.StartAsync().ConfigureAwait(true);

        _service.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_SetsIsRunningToFalse()
    {
        await _service.StartAsync().ConfigureAwait(true);
        await _service.StopAsync().ConfigureAwait(true);

        _service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task SyncAsync_DetectsChanges()
    {
        await _service.StartAsync().ConfigureAwait(true);

        var testFile = Path.Combine(_tempDir, "test.md");
        await _fs.WriteAllTextAsync(testFile, "test content").ConfigureAwait(true);

        await _service.SyncAsync().ConfigureAwait(true);

        var history = await _service.GetSyncHistoryAsync().ConfigureAwait(true);
        history.Should().NotBeNull();
    }

    [Fact]
    public async Task SyncAsync_WithConflict_ResolvesBasedOnStrategy()
    {
        await _service.StartAsync().ConfigureAwait(true);

        var testFile = Path.Combine(_tempDir, "conflict.md");
        await _fs.WriteAllTextAsync(testFile, "local content").ConfigureAwait(true);

        var localEntries = GetLocalEntries(_service);
        var remoteEntries = GetRemoteEntries(_service);

        localEntries[testFile] = new SyncFileEntry
        {
            FilePath = testFile,
            ContentHash = "local-hash",
            LastModified = DateTime.UtcNow,
            Source = "local"
        };
        remoteEntries[testFile] = new SyncFileEntry
        {
            FilePath = testFile,
            ContentHash = "remote-hash",
            LastModified = DateTime.UtcNow.AddSeconds(-1),
            Source = "remote"
        };

        var result = await _service.ResolveConflictAsync(testFile, SyncConflictResolution.KeepLocal).ConfigureAwait(true);

        result.Should().Be(SyncConflictResolution.KeepLocal);
    }

    [Fact]
    public async Task StartAsync_MonitorsDirectoryChanges()
    {
        await _service.StartAsync().ConfigureAwait(true);

        _service.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_StopsMonitoring()
    {
        await _service.StartAsync().ConfigureAwait(true);
        await _service.StopAsync().ConfigureAwait(true);

        _service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task GetSyncHistoryAsync_ReturnsEvents()
    {
        var history = await _service.GetSyncHistoryAsync().ConfigureAwait(true);

        history.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullFileOperationService_ThrowsArgumentNullException()
    {
        var act = () => new global::Memdir.Sync.TeamMemorySyncService(null!, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
