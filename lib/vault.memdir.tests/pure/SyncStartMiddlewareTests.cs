
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Tests.Memdir;

/// <summary>
/// 6 个团队同步启动中间件确定性测试 — InMemoryFileSystem + Mock IFileOperationService。
/// 每个中间件独立测试类,验证短路/放行/上下文填充逻辑,不依赖时序。
/// </summary>

/// <summary>PathValidationMiddleware — 路径校验:WatchPath 空则 Fail,目录不存在则创建,否则放行。</summary>
[Trait("Category", "Deterministic")]
public sealed class PathValidationMiddlewareTests {
    private readonly IO.FileSystem.InMemoryFileSystem _fs = new();

    private static SyncStartContext Ctx(IFileSystem fs, TeamMemorySyncOptions? options = null) {
        options ??= new TeamMemorySyncOptions { WatchPath = "/watch/" };
        return new SyncStartContext {
            FileSystem = fs,
            FileOperationService = new Mock<IFileOperationService>().Object,
            Options = options,
            IsDisposed = false,
            IsAlreadyRunning = false,
            LocalEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            RemoteEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            SyncHistory = new ConcurrentQueue<MemorySyncEvent>()
        };
    }

    [Fact]
    public async Task InvokeAsync_EmptyWatchPath_FailsAndShortCircuitsNext() {
        await using var mw = new PathValidationMiddleware();
        var ctx = Ctx(_fs, new TeamMemorySyncOptions { WatchPath = "" });
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeTrue();
        ctx.ErrorMessage.Should().NotBeNullOrEmpty();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_DirectoryExists_CallsNextWithoutCreating() {
        await using var mw = new PathValidationMiddleware();
        var dir = "/watch/";
        _fs.CreateDirectory(dir);
        var ctx = Ctx(_fs, new TeamMemorySyncOptions { WatchPath = dir });
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeFalse();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_DirectoryNotExists_CreatesDirectoryAndCallsNext() {
        await using var mw = new PathValidationMiddleware();
        var dir = "/watch/new/";
        var ctx = Ctx(_fs, new TeamMemorySyncOptions { WatchPath = dir });
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        _fs.DirectoryExists(dir).Should().BeFalse();
        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        _fs.DirectoryExists(dir).Should().BeTrue();
        nextCalled.Should().BeTrue();
    }
}

/// <summary>AutoSyncMiddleware — 启用自动同步时配置 SyncTimer,否则直接放行。</summary>
[Trait("Category", "Deterministic")]
public sealed class AutoSyncMiddlewareTests {
    private static SyncStartContext Ctx(TeamMemorySyncOptions? options = null, Action? startAutoSync = null) {
        options ??= new TeamMemorySyncOptions { WatchPath = "/watch/", EnableAutoSync = true, SyncInterval = TimeSpan.FromSeconds(30) };
        return new SyncStartContext {
            FileSystem = new IO.FileSystem.InMemoryFileSystem(),
            FileOperationService = new Mock<IFileOperationService>().Object,
            Options = options,
            IsDisposed = false, IsAlreadyRunning = false,
            LocalEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            RemoteEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            SyncHistory = new ConcurrentQueue<MemorySyncEvent>(),
            StartAutoSync = startAutoSync
        };
    }

    [Fact]
    public async Task InvokeAsync_EnableAutoSyncTrueWithCallback_CallsNext() {
        await using var mw = new AutoSyncMiddleware();
        var callbackCalled = false;
        var ctx = Ctx(startAutoSync: () => callbackCalled = true);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);
        nextCalled.Should().BeTrue();
        callbackCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_EnableAutoSyncFalse_DoesNotCallCallbackAndCallsNext() {
        await using var mw = new AutoSyncMiddleware();
        var callbackCalled = false;
        var ctx = Ctx(options: new TeamMemorySyncOptions { WatchPath = "/watch/", EnableAutoSync = false }, startAutoSync: () => callbackCalled = true);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);
        nextCalled.Should().BeTrue();
        callbackCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_NullStartAutoSync_CallsNextWithoutThrowing() {
        await using var mw = new AutoSyncMiddleware();
        var ctx = Ctx(startAutoSync: null);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        var act = async () => await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
        nextCalled.Should().BeTrue();
    }
}

/// <summary>DisposedCheckMiddleware — 已释放或已运行时短路,否则放行。</summary>
[Trait("Category", "Deterministic")]
public sealed class DisposedCheckMiddlewareTests {
    private static SyncStartContext Ctx(bool disposed = false, bool running = false) => new() {
        FileSystem = new IO.FileSystem.InMemoryFileSystem(),
        FileOperationService = new Mock<IFileOperationService>().Object,
        Options = new TeamMemorySyncOptions { WatchPath = "/watch/" },
        IsDisposed = disposed, IsAlreadyRunning = running,
        LocalEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
        RemoteEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
        SyncHistory = new ConcurrentQueue<MemorySyncEvent>()
    };

    [Fact]
    public async Task InvokeAsync_IsDisposed_FailsAndShortCircuits() {
        await using var mw = new DisposedCheckMiddleware();
        var ctx = Ctx(disposed: true);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeTrue();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_IsAlreadyRunning_ShortCircuitsWithoutFail() {
        await using var mw = new DisposedCheckMiddleware();
        var ctx = Ctx(running: true);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeFalse();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Normal_CallsNext() {
        await using var mw = new DisposedCheckMiddleware();
        var ctx = Ctx();
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);
        nextCalled.Should().BeTrue();
    }
}

/// <summary>FileWatcherMiddleware — 启用文件监控时创建 IFileSystemWatcher 并挂到上下文,否则放行。</summary>
[Trait("Category", "Deterministic")]
public sealed class FileWatcherMiddlewareTests {
    private readonly IO.FileSystem.InMemoryFileSystem _fs = new();

    private SyncStartContext Ctx(bool enableWatch, string watchPath = "/watch/")
        => new() {
            FileSystem = _fs,
            FileOperationService = new Mock<IFileOperationService>().Object,
            Options = new TeamMemorySyncOptions { WatchPath = watchPath, EnableFileWatching = enableWatch, FilePatterns = new() { "*.md", "*.json" } },
            IsDisposed = false, IsAlreadyRunning = false,
            LocalEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            RemoteEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            SyncHistory = new ConcurrentQueue<MemorySyncEvent>()
        };

    [Fact]
    public async Task InvokeAsync_EnableFileWatchingFalse_CallsNextWithoutCreatingWatcher() {
        await using var mw = new FileWatcherMiddleware();
        var ctx = Ctx(enableWatch: false);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        ctx.Watcher.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_EnableFileWatchingTrue_CreatesWatcherAndAssignsToContext() {
        await using var mw = new FileWatcherMiddleware();
        var ctx = Ctx(enableWatch: true);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        ctx.Watcher.Should().NotBeNull();
        ctx.Watcher!.IncludeSubdirectories.Should().BeTrue();
        ctx.Watcher.EnableRaisingEvents.Should().BeTrue();
        ctx.Watcher.Filters.Should().Contain("*.md", "*.json");
    }
}

/// <summary>LocalScanMiddleware — 扫描 WatchPath 下文件填充 LocalEntries。</summary>
[Trait("Category", "Deterministic")]
public sealed class LocalScanMiddlewareTests {
    private readonly IO.FileSystem.InMemoryFileSystem _fs = new();

    private SyncStartContext Ctx(string watchPath, List<string>? patterns = null)
        => new() {
            FileSystem = _fs,
            FileOperationService = new Mock<IFileOperationService>().Object,
            Options = new TeamMemorySyncOptions { WatchPath = watchPath, FilePatterns = patterns ?? new() { "*.md" } },
            IsDisposed = false, IsAlreadyRunning = false,
            LocalEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            RemoteEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            SyncHistory = new ConcurrentQueue<MemorySyncEvent>()
        };

    [Fact]
    public async Task InvokeAsync_EmptyWatchPath_CallsNextWithoutScan() {
        await using var mw = new LocalScanMiddleware();
        var ctx = Ctx("");
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);
        nextCalled.Should().BeTrue();
        ctx.LocalEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_DirectoryNotExists_CallsNextWithoutScan() {
        await using var mw = new LocalScanMiddleware();
        var ctx = Ctx("/nonexistent/");
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);
        nextCalled.Should().BeTrue();
        ctx.LocalEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ValidFiles_PopulatesLocalEntriesAndCallsNext() {
        var dir = "/watch/";
        _fs.CreateDirectory(dir);
        await _fs.WriteAllTextAsync(dir + "a.md", "alpha").ConfigureAwait(true);
        await _fs.WriteAllTextAsync(dir + "b.md", "beta").ConfigureAwait(true);
        await _fs.WriteAllTextAsync(dir + "c.txt", "gamma").ConfigureAwait(true);

        await using var mw = new LocalScanMiddleware();
        var ctx = Ctx(dir, patterns: new() { "*.md" });
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        ctx.LocalEntries.Should().HaveCount(2);
        var names = ctx.LocalEntries.Values.Select(e => Path.GetFileName(e.FilePath)).ToList();
        names.Should().Contain("a.md", "b.md");
        foreach (var entry in ctx.LocalEntries.Values) {
            entry.Source.Should().Be("local");
            entry.ContentHash.Should().NotBeNullOrEmpty();
        }
    }
}

/// <summary>RemoteScanMiddleware — 从远程存储读取索引填充 RemoteEntries,异常静默。</summary>
[Trait("Category", "Deterministic")]
public sealed class RemoteScanMiddlewareTests {
    private SyncStartContext Ctx(string remotePath, Mock<IFileOperationService> fosMock)
        => new() {
            FileSystem = new IO.FileSystem.InMemoryFileSystem(),
            FileOperationService = fosMock.Object,
            Options = new TeamMemorySyncOptions { WatchPath = "/watch/", RemoteStoragePath = remotePath },
            IsDisposed = false, IsAlreadyRunning = false,
            LocalEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            RemoteEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            SyncHistory = new ConcurrentQueue<MemorySyncEvent>()
        };

    [Fact]
    public async Task InvokeAsync_EmptyRemoteStoragePath_CallsNextWithoutScan() {
        await using var mw = new RemoteScanMiddleware();
        var ctx = Ctx("", new Mock<IFileOperationService>());
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);
        nextCalled.Should().BeTrue();
        ctx.RemoteEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ReadFails_CallsNextAndLeavesRemoteEmpty() {
        var fosMock = new Mock<IFileOperationService>();
        fosMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(FileReadResult.FailureResult("/remote/index.json", "io error"));
        await using var mw = new RemoteScanMiddleware();
        var ctx = Ctx("/remote/index.json", fosMock);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);
        nextCalled.Should().BeTrue();
        ctx.RemoteEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ValidJson_PopulatesRemoteEntriesAndCallsNext() {
        var entries = new List<SyncFileEntry> {
            new() { FilePath = "/team/x.md", ContentHash = "hx", LastModified = DateTime.UnixEpoch, Source = "remote" }
        };
        var json = RelaxedJsonSerializer.Serialize(entries, TeamMemorySyncJsonContext.Default);
        var fosMock = new Mock<IFileOperationService>();
        fosMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(FileReadResult.SuccessResult("/remote/index.json", json, 1, 0, 1));

        await using var mw = new RemoteScanMiddleware();
        var ctx = Ctx("/remote/index.json", fosMock);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        ctx.RemoteEntries.Should().ContainSingle();
        ctx.RemoteEntries["/team/x.md"].ContentHash.Should().Be("hx");
    }
}

/// <summary>StartCompletionMiddleware — 未失败时标记 MarkAsRunning 并放行,失败时仅放行。</summary>
[Trait("Category", "Deterministic")]
public sealed class StartCompletionMiddlewareTests {
    private static SyncStartContext Ctx(bool failed) {
        var ctx = new SyncStartContext {
            FileSystem = new IO.FileSystem.InMemoryFileSystem(),
            FileOperationService = new Mock<IFileOperationService>().Object,
            Options = new TeamMemorySyncOptions { WatchPath = "/watch/" },
            IsDisposed = false, IsAlreadyRunning = false,
            LocalEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            RemoteEntries = new ConcurrentDictionary<string, SyncFileEntry>(),
            SyncHistory = new ConcurrentQueue<MemorySyncEvent>()
        };
        if (failed) ctx.Fail("upstream error");
        return ctx;
    }

    [Fact]
    public async Task InvokeAsync_FailedContext_DoesNotMarkAsRunningButCallsNext() {
        await using var mw = new StartCompletionMiddleware();
        var ctx = Ctx(failed: true);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        ctx.MarkAsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_SuccessContext_MarksAsRunningAndCallsNext() {
        await using var mw = new StartCompletionMiddleware();
        var ctx = Ctx(failed: false);
        var nextCalled = false;
        Task Next(SyncStartContext c, CancellationToken ct) { nextCalled = true; return Task.CompletedTask; }

        await mw.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        ctx.MarkAsRunning.Should().BeTrue();
    }
}
