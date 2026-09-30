
namespace Core.Tests.Memdir;

/// <summary>
/// SyncFileScanner 确定性测试 — 用 InMemoryFileSystem 消除文件 IO,Mock IFileOperationService 消除远程读。
/// 覆盖 ScanLocalAsync/ScanRemoteAsync 的纯扫描逻辑(目录存在性/模式遍历/JSON 反序列化填充)。
/// </summary>
[Trait("Category", "Deterministic")]
public sealed class SyncFileScannerTests {
    private readonly IO.FileSystem.InMemoryFileSystem _fs = new();
    private readonly ConcurrentDictionary<string, SyncFileEntry> _localEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SyncFileEntry> _remoteEntries = new(StringComparer.OrdinalIgnoreCase);

    private static TeamMemorySyncOptions Options(string watchPath = "", string remotePath = "", List<string>? patterns = null)
        => new() {
            WatchPath = watchPath,
            RemoteStoragePath = remotePath,
            FilePatterns = patterns ?? new() { "*.md", "*.json", "*.txt" }
        };

    private SyncFileScanner CreateScanner(TeamMemorySyncOptions options, Mock<IFileOperationService>? fosMock = null) {
        fosMock ??= new Mock<IFileOperationService>();
        return new SyncFileScanner(_fs, fosMock.Object, options, logger: null, _localEntries, _remoteEntries);
    }

    [Fact]
    public async Task ScanLocalAsync_EmptyWatchPath_NoOpAndNoException() {
        var scanner = CreateScanner(Options(watchPath: ""));
        await scanner.ScanLocalAsync(CancellationToken.None).ConfigureAwait(true);
        _localEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanLocalAsync_DirectoryNotExists_NoOpAndNoException() {
        var scanner = CreateScanner(Options(watchPath: "/nonexistent/dir/"));
        await scanner.ScanLocalAsync(CancellationToken.None).ConfigureAwait(true);
        _localEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanLocalAsync_SingleMdFile_PopulatesLocalEntriesWithHashAndTimestamp() {
        var dir = "/scan/single/";
        _fs.CreateDirectory(dir);
        var file = dir + "a.md";
        await _fs.WriteAllTextAsync(file, "hello").ConfigureAwait(true);

        var scanner = CreateScanner(Options(watchPath: dir, patterns: new() { "*.md" }));
        await scanner.ScanLocalAsync(CancellationToken.None).ConfigureAwait(true);

        _localEntries.Should().ContainSingle();
        var entry = _localEntries.Values.Single();
        entry.FilePath.Should().EndWith("a.md");
        entry.Source.Should().Be("local");
        entry.ContentHash.Should().NotBeNullOrEmpty();
        // 哈希应与 SyncFileHash.ComputeAsync 一致(确定性)
        var expected = await SyncFileHash.ComputeAsync(_fs, entry.FilePath).ConfigureAwait(true);
        entry.ContentHash.Should().Be(expected);
    }

    [Fact]
    public async Task ScanLocalAsync_MultiplePatterns_AggregatesFilesAcrossPatterns() {
        var dir = "/scan/multi/";
        _fs.CreateDirectory(dir);
        await _fs.WriteAllTextAsync(dir + "a.md", "md").ConfigureAwait(true);
        await _fs.WriteAllTextAsync(dir + "b.json", "{}").ConfigureAwait(true);
        await _fs.WriteAllTextAsync(dir + "c.txt", "txt").ConfigureAwait(true);
        // 不匹配的扩展名
        await _fs.WriteAllTextAsync(dir + "d.log", "log").ConfigureAwait(true);

        var scanner = CreateScanner(Options(watchPath: dir, patterns: new() { "*.md", "*.json", "*.txt" }));
        await scanner.ScanLocalAsync(CancellationToken.None).ConfigureAwait(true);

        _localEntries.Should().HaveCount(3);
        var names = _localEntries.Values.Select(e => Path.GetFileName(e.FilePath)).ToList();
        names.Should().Contain("a.md", "b.json", "c.txt");
        names.Should().NotContain("d.log");
    }

    [Fact]
    public async Task ScanLocalAsync_EmptyDirectory_LeavesEntriesEmpty() {
        var dir = "/scan/empty/";
        _fs.CreateDirectory(dir);
        var scanner = CreateScanner(Options(watchPath: dir));
        await scanner.ScanLocalAsync(CancellationToken.None).ConfigureAwait(true);
        _localEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanLocalAsync_PatternWithNoMatch_LeavesEntriesEmpty() {
        var dir = "/scan/nomatch/";
        _fs.CreateDirectory(dir);
        await _fs.WriteAllTextAsync(dir + "a.md", "x").ConfigureAwait(true);
        var scanner = CreateScanner(Options(watchPath: dir, patterns: new() { "*.csv" }));
        await scanner.ScanLocalAsync(CancellationToken.None).ConfigureAwait(true);
        _localEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanLocalAsync_OverwritesExistingEntry_ForSameFilePath() {
        var dir = "/scan/overwrite/";
        _fs.CreateDirectory(dir);
        var file = dir + "a.md";
        await _fs.WriteAllTextAsync(file, "first").ConfigureAwait(true);
        // 用 GetFiles 返回的实际路径作 key,保证与 scanner 内部 key 一致
        var actualFile = _fs.GetFiles(dir, "*.md", SearchOption.AllDirectories).Single();
        _localEntries[actualFile] = new SyncFileEntry {
            FilePath = actualFile, ContentHash = "stale", LastModified = DateTime.UnixEpoch, Source = "stale"
        };

        var scanner = CreateScanner(Options(watchPath: dir, patterns: new() { "*.md" }));
        await scanner.ScanLocalAsync(CancellationToken.None).ConfigureAwait(true);

        _localEntries[actualFile].ContentHash.Should().NotBe("stale");
        _localEntries[actualFile].Source.Should().Be("local");
    }

    [Fact]
    public async Task ScanRemoteAsync_EmptyRemoteStoragePath_NoOp() {
        var scanner = CreateScanner(Options(remotePath: ""));
        await scanner.ScanRemoteAsync(CancellationToken.None).ConfigureAwait(true);
        _remoteEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanRemoteAsync_ReadFails_LeavesEntriesEmptyAndNoThrow() {
        var fosMock = new Mock<IFileOperationService>();
        fosMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(FileReadResult.FailureResult("/remote/index.json", "not found"));

        var scanner = CreateScanner(Options(remotePath: "/remote/index.json"), fosMock);
        await scanner.ScanRemoteAsync(CancellationToken.None).ConfigureAwait(true);
        _remoteEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanRemoteAsync_EmptyContent_LeavesEntriesEmpty() {
        var fosMock = new Mock<IFileOperationService>();
        fosMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(FileReadResult.SuccessResult("/remote/index.json", "", 0, 0, 0));

        var scanner = CreateScanner(Options(remotePath: "/remote/index.json"), fosMock);
        await scanner.ScanRemoteAsync(CancellationToken.None).ConfigureAwait(true);
        _remoteEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanRemoteAsync_ValidJson_PopulatesRemoteEntries() {
        var entries = new List<SyncFileEntry> {
            new() { FilePath = "/team/a.md", ContentHash = "h1", LastModified = new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc), Source = "remote" },
            new() { FilePath = "/team/b.md", ContentHash = "h2", LastModified = new DateTime(2026,1,2,0,0,0,DateTimeKind.Utc), Source = "remote" }
        };
        var json = RelaxedJsonSerializer.Serialize(entries, TeamMemorySyncJsonContext.Default);

        var fosMock = new Mock<IFileOperationService>();
        fosMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(FileReadResult.SuccessResult("/remote/index.json", json, 1, 0, 1));

        var scanner = CreateScanner(Options(remotePath: "/remote/index.json"), fosMock);
        await scanner.ScanRemoteAsync(CancellationToken.None).ConfigureAwait(true);

        _remoteEntries.Should().HaveCount(2);
        _remoteEntries["/team/a.md"].ContentHash.Should().Be("h1");
        _remoteEntries["/team/b.md"].ContentHash.Should().Be("h2");
    }

    [Fact]
    public async Task ScanRemoteAsync_InvalidJson_SilentlySkipsAndNoThrow() {
        var fosMock = new Mock<IFileOperationService>();
        fosMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(FileReadResult.SuccessResult("/remote/index.json", "{invalid json", 1, 0, 1));

        var scanner = CreateScanner(Options(remotePath: "/remote/index.json"), fosMock);
        var act = async () => await scanner.ScanRemoteAsync(CancellationToken.None).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
        _remoteEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanRemoteAsync_NullJsonArray_LeavesEntriesEmpty() {
        // JSON "null" 反序列化为 null List
        var fosMock = new Mock<IFileOperationService>();
        fosMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(FileReadResult.SuccessResult("/remote/index.json", "null", 1, 0, 1));

        var scanner = CreateScanner(Options(remotePath: "/remote/index.json"), fosMock);
        await scanner.ScanRemoteAsync(CancellationToken.None).ConfigureAwait(true);
        _remoteEntries.Should().BeEmpty();
    }
}
