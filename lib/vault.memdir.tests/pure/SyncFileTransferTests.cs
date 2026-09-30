
namespace Core.Tests.Memdir;

/// <summary>
/// SyncFileTransfer 确定性测试 — InMemoryFileSystem + Mock IFileOperationService + FakeClockService。
/// 覆盖 PushToRemoteAsync/PullFromRemoteAsync/PersistRemoteIndexAsync 的文件传输与索引持久化逻辑。
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class SyncFileTransferTests {
    private readonly IO.FileSystem.InMemoryFileSystem _fs = new();
    private readonly ConcurrentDictionary<string, SyncFileEntry> _localEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SyncFileEntry> _remoteEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly SyncEventLog _eventLog = new();
    private readonly FakeClockService _clock = new(new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc));
    private readonly Mock<IFileOperationService> _fosMock = new();

    private static TeamMemorySyncOptions Options(string watchPath = "/watch/", string remotePath = "/remote/index.json")
        => new() { WatchPath = watchPath, RemoteStoragePath = remotePath };

    private SyncFileTransfer CreateTransfer(TeamMemorySyncOptions? options = null)
        => new(_fs, _fosMock.Object, options ?? Options(), _clock, logger: null, _localEntries, _remoteEntries, _eventLog);

    [Fact]
    public async Task PushToRemoteAsync_EmptyRemoteStoragePath_NoOp() {
        var transfer = CreateTransfer(Options(remotePath: ""));
        var act = async () => await transfer.PushToRemoteAsync("/watch/a.md", CancellationToken.None).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
        _remoteEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task PushToRemoteAsync_ValidFile_UpdatesRemoteEntriesPersistsAndLogsSyncedEvent() {
        var dir = "/watch/";
        _fs.CreateDirectory(dir);
        var file = dir + "a.md";
        await _fs.WriteAllTextAsync(file, "content").ConfigureAwait(true);

        _fosMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FileWriteResult.SuccessResult("/remote/index.json", "{}", "write"));

        var transfer = CreateTransfer();
        await transfer.PushToRemoteAsync(file, CancellationToken.None).ConfigureAwait(true);

        // 1. remote entries 更新
        _remoteEntries.Should().ContainKey(file);
        var entry = _remoteEntries[file];
        entry.FilePath.Should().Be(file);
        entry.Source.Should().Be("local");
        entry.ContentHash.Should().NotBeNullOrEmpty();

        // 2. PersistRemoteIndexAsync 调用了 WriteFileAsync
        _fosMock.Verify(x => x.WriteFileAsync("/remote/index.json", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // 3. 事件入队
        var events = _eventLog.GetRecent(10).ToList();
        events.Should().ContainSingle(e => e.FilePath == file && e.Type == SyncEventType.Synced);
    }

    [Fact]
    public async Task PushToRemoteAsync_FileNotExists_SilentlySkipsNoThrow() {
        var transfer = CreateTransfer();
        var act = async () => await transfer.PushToRemoteAsync("/watch/missing.md", CancellationToken.None).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
        _remoteEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task PullFromRemoteAsync_EmptyRemoteStoragePath_NoOp() {
        var transfer = CreateTransfer(Options(remotePath: ""));
        var act = async () => await transfer.PullFromRemoteAsync("/watch/a.md", CancellationToken.None).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
        _localEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task PullFromRemoteAsync_NoRemoteEntry_NoOp() {
        var transfer = CreateTransfer();
        await transfer.PullFromRemoteAsync("/watch/unknown.md", CancellationToken.None).ConfigureAwait(true);
        _localEntries.Should().BeEmpty();
        _fosMock.Verify(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PullFromRemoteAsync_ReadFails_LeavesLocalUnchangedNoThrow() {
        var file = "/watch/a.md";
        _remoteEntries[file] = new SyncFileEntry {
            FilePath = file, ContentHash = "rh", LastModified = _clock.GetUtcNow(), Source = "remote"
        };
        _fosMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FileReadResult.FailureResult("/remote/a.md", "not found"));

        var transfer = CreateTransfer();
        await transfer.PullFromRemoteAsync(file, CancellationToken.None).ConfigureAwait(true);

        _localEntries.Should().BeEmpty();
        _fs.FileExists(file).Should().BeFalse();
    }

    [Fact]
    public async Task PullFromRemoteAsync_ValidEntry_WritesLocalFileAndUpdatesLocalEntries() {
        var file = "/watch/sub/a.md";
        _remoteEntries[file] = new SyncFileEntry {
            FilePath = file, ContentHash = "rh", LastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Source = "remote"
        };
        _fosMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FileReadResult.SuccessResult("/remote/a.md", "remote content", 1, 0, 1));

        var transfer = CreateTransfer();
        await transfer.PullFromRemoteAsync(file, CancellationToken.None).ConfigureAwait(true);

        // 本地文件写入
        _fs.FileExists(file).Should().BeTrue();
        (await _fs.ReadAllTextAsync(file).ConfigureAwait(true)).Should().Be("remote content");

        // local entries 更新
        _localEntries.Should().ContainKey(file);
        _localEntries[file].Source.Should().Be("remote");
        _localEntries[file].ContentHash.Should().Be("rh");

        // 事件入队
        var events = _eventLog.GetRecent(10).ToList();
        events.Should().ContainSingle(e => e.FilePath == file && e.Type == SyncEventType.Synced);
    }

    [Fact]
    public async Task PersistRemoteIndexAsync_EmptyRemoteStoragePath_NoOp() {
        var transfer = CreateTransfer(Options(remotePath: ""));
        await transfer.PersistRemoteIndexAsync(CancellationToken.None).ConfigureAwait(true);
        _fosMock.Verify(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PersistRemoteIndexAsync_ValidEntries_WritesJsonToRemoteStorage() {
        _remoteEntries["/watch/a.md"] = new SyncFileEntry {
            FilePath = "/watch/a.md", ContentHash = "h1", LastModified = DateTime.UnixEpoch, Source = "local"
        };
        string? capturedJson = null;
        _fosMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string p, string c, CancellationToken _) => capturedJson = c)
                .ReturnsAsync(FileWriteResult.SuccessResult("/remote/index.json", "{}", "write"));

        var transfer = CreateTransfer();
        await transfer.PersistRemoteIndexAsync(CancellationToken.None).ConfigureAwait(true);

        _fosMock.Verify(x => x.WriteFileAsync("/remote/index.json", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        capturedJson.Should().NotBeNullOrEmpty();
        // 反序列化回放应包含原条目
        var roundtrip = RelaxedJsonSerializer.Deserialize(capturedJson!, TeamMemorySyncJsonContext.Default.ListSyncFileEntry);
        roundtrip.Should().NotBeNull();
        roundtrip!.Should().ContainSingle(e => e.FilePath == "/watch/a.md" && e.ContentHash == "h1");
    }

    [Fact]
    public async Task PersistRemoteIndexAsync_EmptyEntries_WritesEmptyArrayJson() {
        string? capturedJson = null;
        _fosMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string p, string c, CancellationToken _) => capturedJson = c)
                .ReturnsAsync(FileWriteResult.SuccessResult("/remote/index.json", "[]", "write"));

        var transfer = CreateTransfer();
        await transfer.PersistRemoteIndexAsync(CancellationToken.None).ConfigureAwait(true);

        capturedJson.Should().NotBeNull();
        var roundtrip = RelaxedJsonSerializer.Deserialize(capturedJson!, TeamMemorySyncJsonContext.Default.ListSyncFileEntry);
        roundtrip.Should().NotBeNull();
        roundtrip!.Should().BeEmpty();
    }

    [Fact]
    public async Task PushToRemoteAsync_EventTimestampUsesClock() {
        var dir = "/watch/";
        _fs.CreateDirectory(dir);
        var file = dir + "ts.md";
        await _fs.WriteAllTextAsync(file, "x").ConfigureAwait(true);
        _fosMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FileWriteResult.SuccessResult("/remote/index.json", "[]", "write"));

        var transfer = CreateTransfer();
        await transfer.PushToRemoteAsync(file, CancellationToken.None).ConfigureAwait(true);

        var evt = _eventLog.GetRecent(1).Single();
        evt.Timestamp.Should().Be(_clock.GetUtcNow());
    }
}
