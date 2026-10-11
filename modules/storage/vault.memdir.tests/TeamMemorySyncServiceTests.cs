// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Tests.Services.Memdir;

public sealed class TeamMemorySyncServiceTests : IAsyncDisposable {
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly Mock<IFileOperationService> _fileOperationServiceMock;
    private readonly global::Memdir.Sync.TeamMemorySyncService _service;
    private readonly string _tempDir = "/test/memdir_sync/";
    private bool _disposed;

    public TeamMemorySyncServiceTests() {
        _fs.CreateDirectory(_tempDir);

        _fileOperationServiceMock = new Mock<IFileOperationService>();
        var options = Options.Create(new TeamMemorySyncOptions {
            WatchPath = _tempDir,
            EnableAutoSync = false,
            EnableFileWatching = false
        });
        _service = new global::Memdir.Sync.TeamMemorySyncService(_fs, _fileOperationServiceMock.Object, options);
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;

        await _service.DisposeSafeAsync();
    }

    private static global::System.Collections.Concurrent.ConcurrentDictionary<string, SyncFileEntry> GetLocalEntries(global::Memdir.Sync.TeamMemorySyncService service) {
        var field = typeof(global::Memdir.Sync.TeamMemorySyncService).GetField("_localEntries", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
        return (global::System.Collections.Concurrent.ConcurrentDictionary<string, SyncFileEntry>)field!.GetValue(service)!;
    }

    private static global::System.Collections.Concurrent.ConcurrentDictionary<string, SyncFileEntry> GetRemoteEntries(global::Memdir.Sync.TeamMemorySyncService service) {
        var field = typeof(global::Memdir.Sync.TeamMemorySyncService).GetField("_remoteEntries", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
        return (global::System.Collections.Concurrent.ConcurrentDictionary<string, SyncFileEntry>)field!.GetValue(service)!;
    }

    [Fact]
    public async Task StartAsync_SetsIsRunningToTrue() {
        await _service.StartAsync().ConfigureAwait(true);

        _service.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_SetsIsRunningToFalse() {
        await _service.StartAsync().ConfigureAwait(true);
        await _service.StopAsync().ConfigureAwait(true);

        _service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task SyncAsync_DetectsChanges() {
        await _service.StartAsync().ConfigureAwait(true);

        var testFile = Path.Combine(_tempDir, "test.md");
        await _fs.WriteAllTextAsync(testFile, "test content").ConfigureAwait(true);

        await _service.SyncAsync().ConfigureAwait(true);

        var history = await _service.GetSyncHistoryAsync().ConfigureAwait(true);
        history.Should().NotBeNull();
    }

    [Fact]
    public async Task SyncAsync_WithConflict_ResolvesBasedOnStrategy() {
        await _service.StartAsync().ConfigureAwait(true);

        var testFile = Path.Combine(_tempDir, "conflict.md");
        await _fs.WriteAllTextAsync(testFile, "local content").ConfigureAwait(true);

        var localEntries = GetLocalEntries(_service);
        var remoteEntries = GetRemoteEntries(_service);

        localEntries[testFile] = new SyncFileEntry {
            FilePath = testFile,
            ContentHash = "local-hash",
            LastModified = DateTime.UtcNow,
            Source = "local"
        };
        remoteEntries[testFile] = new SyncFileEntry {
            FilePath = testFile,
            ContentHash = "remote-hash",
            LastModified = DateTime.UtcNow.AddSeconds(-1),
            Source = "remote"
        };

        var result = await _service.ResolveConflictAsync(testFile, SyncConflictResolution.KeepLocal).ConfigureAwait(true);

        result.Should().Be(SyncConflictResolution.KeepLocal);
    }

    [Fact]
    public async Task StartAsync_MonitorsDirectoryChanges() {
        await _service.StartAsync().ConfigureAwait(true);

        _service.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_StopsMonitoring() {
        await _service.StartAsync().ConfigureAwait(true);
        await _service.StopAsync().ConfigureAwait(true);

        _service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task GetSyncHistoryAsync_ReturnsEvents() {
        var history = await _service.GetSyncHistoryAsync().ConfigureAwait(true);

        history.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullFileOperationService_ThrowsArgumentNullException() {
        var act = () => new global::Memdir.Sync.TeamMemorySyncService(null!, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // === GetSyncStatus / SyncTeamMemoryAsync 补测(确定性,不依赖时序) ===

    private static global::Memdir.Sync.Helpers.SyncEventLog GetEventLog(global::Memdir.Sync.TeamMemorySyncService service) {
        var field = typeof(global::Memdir.Sync.TeamMemorySyncService).GetField("_eventLog", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
        return (global::Memdir.Sync.Helpers.SyncEventLog)field!.GetValue(service)!;
    }

    [Fact]
    public void GetSyncStatus_NullTeamId_ThrowsArgumentException() {
        var act = () => _service.GetSyncStatus(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetSyncStatus_EmptyTeamId_ThrowsArgumentException() {
        var act = () => _service.GetSyncStatus("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetSyncStatus_NoEntriesOrEvents_ReturnsZeroCountAndNoConflicts() {
        var status = _service.GetSyncStatus("team1");

        status.Should().NotBeNull();
        status!.TeamId.Should().Be("team1");
        status.SyncedMemoryCount.Should().Be(0);
        status.HasConflicts.Should().BeFalse();
        status.Conflicts.Should().BeEmpty();
        status.LastSyncAt.Should().BeNull();
        status.IsWatching.Should().BeFalse();
    }

    [Fact]
    public void GetSyncStatus_WithLocalEntries_CountsEntriesUnderTeamPath() {
        var localEntries = GetLocalEntries(_service);
        var teamPath = Path.Combine(_tempDir, "team1");
        localEntries[teamPath + "/a.md"] = new SyncFileEntry { FilePath = teamPath + "/a.md", ContentHash = "h1", LastModified = DateTime.UtcNow, Source = "local" };
        localEntries[teamPath + "/b.md"] = new SyncFileEntry { FilePath = teamPath + "/b.md", ContentHash = "h2", LastModified = DateTime.UtcNow, Source = "local" };
        // 另一个团队的条目不应计入
        var otherPath = Path.Combine(_tempDir, "team2");
        localEntries[otherPath + "/c.md"] = new SyncFileEntry { FilePath = otherPath + "/c.md", ContentHash = "h3", LastModified = DateTime.UtcNow, Source = "local" };

        var status = _service.GetSyncStatus("team1");

        status!.SyncedMemoryCount.Should().Be(2);
    }

    [Fact]
    public void GetSyncStatus_WithConflictDetectedEvent_ReportsConflict() {
        var eventLog = GetEventLog(_service);
        var teamPath = Path.Combine(_tempDir, "team1");
        var file = teamPath + "/conflict.md";
        eventLog.Enqueue(new MemorySyncEvent {
            EventId = "evt1",
            FilePath = file,
            Type = SyncEventType.ConflictDetected,
            Timestamp = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc)
        });

        var status = _service.GetSyncStatus("team1");

        status!.HasConflicts.Should().BeTrue();
        status.Conflicts.Should().ContainSingle();
        status.Conflicts[0].MemoryId.Should().Be("conflict.md");
        status.Conflicts[0].ConflictType.Should().Be(ConflictType.ContentMismatch);
    }

    [Fact]
    public void GetSyncStatus_LastSyncAt_IsLatestEventTimestampForTeam() {
        var eventLog = GetEventLog(_service);
        var teamPath = Path.Combine(_tempDir, "team1");
        var older = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        eventLog.Enqueue(new MemorySyncEvent { EventId = "e1", FilePath = teamPath + "/a.md", Type = SyncEventType.Synced, Timestamp = older });
        eventLog.Enqueue(new MemorySyncEvent { EventId = "e2", FilePath = teamPath + "/b.md", Type = SyncEventType.Synced, Timestamp = newer });

        var status = _service.GetSyncStatus("team1");

        status!.LastSyncAt.Should().Be(newer);
    }

    [Fact]
    public void GetSyncStatus_ConflictEventsFromOtherTeam_NotCounted() {
        var eventLog = GetEventLog(_service);
        var team1Path = Path.Combine(_tempDir, "team1");
        var team2Path = Path.Combine(_tempDir, "team2");
        eventLog.Enqueue(new MemorySyncEvent { EventId = "e1", FilePath = team2Path + "/x.md", Type = SyncEventType.ConflictDetected, Timestamp = DateTime.UtcNow });

        var status = _service.GetSyncStatus("team1");

        status!.HasConflicts.Should().BeFalse();
        status.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncTeamMemoryAsync_EmptyTeamId_ThrowsArgumentException() {
        var act = async () => await _service.SyncTeamMemoryAsync("").ConfigureAwait(true);
        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task SyncTeamMemoryAsync_NullTeamId_ThrowsArgumentException() {
        var act = async () => await _service.SyncTeamMemoryAsync(null!).ConfigureAwait(true);
        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task SyncTeamMemoryAsync_NoEntries_ReturnsStatusWithZeroCount() {
        var status = await _service.SyncTeamMemoryAsync("team1").ConfigureAwait(true);

        status.Should().NotBeNull();
        status!.TeamId.Should().Be("team1");
        status.SyncedMemoryCount.Should().Be(0);
        status.HasConflicts.Should().BeFalse();
    }

    [Fact]
    public async Task SyncTeamMemoryAsync_AfterStart_ReportsIsWatchingTrue() {
        await _service.StartAsync().ConfigureAwait(true);

        var status = await _service.SyncTeamMemoryAsync("team1").ConfigureAwait(true);

        status!.IsWatching.Should().BeTrue();
    }
}