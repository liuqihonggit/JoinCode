
namespace Core.Tests.Memdir;

/// <summary>
/// SyncConflictResolver 确定性测试 — 用真实 SyncFileTransfer + InMemoryFileSystem + Mock IFileOperationService。
/// 覆盖 ResolveAsync 各策略分支(KeepLocal/KeepRemote/KeepNewest/Merge)与缺失条目短路。
/// </summary>
public sealed class SyncConflictResolverTests {
    private readonly IO.FileSystem.InMemoryFileSystem _fs = new();
    private readonly ConcurrentDictionary<string, SyncFileEntry> _localEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SyncFileEntry> _remoteEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly SyncEventLog _eventLog = new();
    private readonly FakeClockService _clock = new(new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc));
    private readonly Mock<IFileOperationService> _fosMock = new();

    private static TeamMemorySyncOptions Options()
        => new() { WatchPath = "/watch/", RemoteStoragePath = "/remote/index.json" };

    private (SyncFileTransfer transfer, SyncConflictResolver resolver) CreatePair() {
        var transfer = new SyncFileTransfer(_fs, _fosMock.Object, Options(), _clock, logger: null, _localEntries, _remoteEntries, _eventLog);
        var resolver = new SyncConflictResolver(transfer, _clock, logger: null, _localEntries, _remoteEntries, _eventLog);
        return (transfer, resolver);
    }

    private void SetupWriteSuccess()
        => _fosMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(FileWriteResult.SuccessResult("/remote/index.json", "[]", "write"));

    private void SetupReadSuccess(string content)
        => _fosMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(FileReadResult.SuccessResult("/remote/a.md", content, 1, 0, 1));

    [Fact]
    public async Task ResolveAsync_LocalEntryMissing_ReturnsResolutionUnchangedAndNoTransfer() {
        var (_, resolver) = CreatePair();
        _remoteEntries["/watch/a.md"] = new SyncFileEntry {
            FilePath = "/watch/a.md", ContentHash = "h", LastModified = DateTime.UnixEpoch, Source = "remote"
        };

        var result = await resolver.ResolveAsync("/watch/a.md", SyncConflictResolution.KeepLocal, CancellationToken.None).ConfigureAwait(true);

        result.Should().Be(SyncConflictResolution.KeepLocal);
        _remoteEntries.Should().HaveCount(1); // 未被 Push 覆盖
        _fosMock.Verify(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_RemoteEntryMissing_ReturnsResolutionUnchangedAndNoTransfer() {
        var (_, resolver) = CreatePair();
        _localEntries["/watch/a.md"] = new SyncFileEntry {
            FilePath = "/watch/a.md", ContentHash = "h", LastModified = DateTime.UnixEpoch, Source = "local"
        };

        var result = await resolver.ResolveAsync("/watch/a.md", SyncConflictResolution.KeepRemote, CancellationToken.None).ConfigureAwait(true);

        result.Should().Be(SyncConflictResolution.KeepRemote);
        _localEntries.Should().HaveCount(1);
        _fosMock.Verify(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_KeepLocal_CallsPushToRemoteAndEnqueuesResolvedEvent() {
        var file = "/watch/a.md";
        _fs.CreateDirectory("/watch/");
        await _fs.WriteAllTextAsync(file, "local").ConfigureAwait(true);
        _localEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "lh", LastModified = DateTime.UnixEpoch, Source = "local" };
        _remoteEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "rh", LastModified = DateTime.UnixEpoch, Source = "remote" };
        SetupWriteSuccess();

        var (_, resolver) = CreatePair();
        var result = await resolver.ResolveAsync(file, SyncConflictResolution.KeepLocal, CancellationToken.None).ConfigureAwait(true);

        result.Should().Be(SyncConflictResolution.KeepLocal);
        // PushToRemote 覆盖了 remote entry 的 hash
        _remoteEntries[file].ContentHash.Should().NotBe("rh");
        _fosMock.Verify(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _eventLog.GetRecent(10).Should().ContainSingle(e => e.FilePath == file && e.Type == SyncEventType.ConflictResolved);
    }

    [Fact]
    public async Task ResolveAsync_KeepRemote_CallsPullFromRemoteAndEnqueuesResolvedEvent() {
        var file = "/watch/a.md";
        _remoteEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "rh", LastModified = DateTime.UnixEpoch, Source = "remote" };
        _localEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "lh", LastModified = DateTime.UnixEpoch, Source = "local" };
        SetupReadSuccess("remote body");

        var (_, resolver) = CreatePair();
        var result = await resolver.ResolveAsync(file, SyncConflictResolution.KeepRemote, CancellationToken.None).ConfigureAwait(true);

        result.Should().Be(SyncConflictResolution.KeepRemote);
        _localEntries[file].ContentHash.Should().Be("rh");
        _localEntries[file].Source.Should().Be("remote");
        _eventLog.GetRecent(10).Should().ContainSingle(e => e.FilePath == file && e.Type == SyncEventType.ConflictResolved);
    }

    [Fact]
    public async Task ResolveAsync_KeepNewest_LocalNewer_CallsPushToRemote() {
        var file = "/watch/a.md";
        _fs.CreateDirectory("/watch/");
        await _fs.WriteAllTextAsync(file, "local").ConfigureAwait(true);
        var localTime = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var remoteTime = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
        _localEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "lh", LastModified = localTime, Source = "local" };
        _remoteEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "rh", LastModified = remoteTime, Source = "remote" };
        SetupWriteSuccess();

        var (_, resolver) = CreatePair();
        await resolver.ResolveAsync(file, SyncConflictResolution.KeepNewest, CancellationToken.None).ConfigureAwait(true);

        _remoteEntries[file].ContentHash.Should().NotBe("rh");
        _fosMock.Verify(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ResolveAsync_KeepNewest_RemoteNewer_CallsPullFromRemote() {
        var file = "/watch/a.md";
        var localTime = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
        var remoteTime = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        _localEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "lh", LastModified = localTime, Source = "local" };
        _remoteEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "rh", LastModified = remoteTime, Source = "remote" };
        SetupReadSuccess("remote body");

        var (_, resolver) = CreatePair();
        await resolver.ResolveAsync(file, SyncConflictResolution.KeepNewest, CancellationToken.None).ConfigureAwait(true);

        _localEntries[file].ContentHash.Should().Be("rh");
        _localEntries[file].Source.Should().Be("remote");
    }

    [Fact]
    public async Task ResolveAsync_KeepNewest_EqualTimestamps_CallsPushToRemoteDueToGreaterOrEqual() {
        var file = "/watch/a.md";
        _fs.CreateDirectory("/watch/");
        await _fs.WriteAllTextAsync(file, "local").ConfigureAwait(true);
        var ts = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        _localEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "lh", LastModified = ts, Source = "local" };
        _remoteEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "rh", LastModified = ts, Source = "remote" };
        SetupWriteSuccess();

        var (_, resolver) = CreatePair();
        await resolver.ResolveAsync(file, SyncConflictResolution.KeepNewest, CancellationToken.None).ConfigureAwait(true);

        // local >= remote → PushToRemote
        _remoteEntries[file].ContentHash.Should().NotBe("rh");
    }

    [Fact]
    public async Task ResolveAsync_Merge_CallsPushToRemote() {
        var file = "/watch/a.md";
        _fs.CreateDirectory("/watch/");
        await _fs.WriteAllTextAsync(file, "local").ConfigureAwait(true);
        _localEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "lh", LastModified = DateTime.UnixEpoch, Source = "local" };
        _remoteEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "rh", LastModified = DateTime.UnixEpoch, Source = "remote" };
        SetupWriteSuccess();

        var (_, resolver) = CreatePair();
        var result = await resolver.ResolveAsync(file, SyncConflictResolution.Merge, CancellationToken.None).ConfigureAwait(true);

        result.Should().Be(SyncConflictResolution.Merge);
        _remoteEntries[file].ContentHash.Should().NotBe("rh");
    }

    [Fact]
    public async Task ResolveAsync_ResolvedEventCarriesConflictResolutionField() {
        var file = "/watch/a.md";
        _fs.CreateDirectory("/watch/");
        await _fs.WriteAllTextAsync(file, "local").ConfigureAwait(true);
        _localEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "lh", LastModified = DateTime.UnixEpoch, Source = "local" };
        _remoteEntries[file] = new SyncFileEntry { FilePath = file, ContentHash = "rh", LastModified = DateTime.UnixEpoch, Source = "remote" };
        SetupWriteSuccess();

        var (_, resolver) = CreatePair();
        await resolver.ResolveAsync(file, SyncConflictResolution.KeepLocal, CancellationToken.None).ConfigureAwait(true);

        var evt = _eventLog.GetRecent(10).First(e => e.Type == SyncEventType.ConflictResolved);
        evt.ConflictResolution.Should().Be(SyncConflictResolution.KeepLocal);
        evt.ErrorMessage.Should().BeNull();
    }
}
