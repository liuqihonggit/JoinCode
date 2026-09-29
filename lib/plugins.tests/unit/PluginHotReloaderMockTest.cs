namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// PluginHotReloader mock 测试 — 验证 HandleStartWatching/HandleReload/HandleReloadAndWait
/// <para>确定性测试:不依赖时序/真实IO,用 InMemoryFileSystem + 真实 PluginManager</para>
/// <para>不依赖 Actor mailbox 的分支(未加载+文件不存在)可直接测试</para>
/// </summary>
public sealed class PluginHotReloaderMockTest {
    private static (PluginHotReloader reloader, IFileSystem fs, PluginManager manager) CreateReloader() {
        var fs = new InMemoryFileSystem();
        var manager = new PluginManager(fs);
        var reloader = new PluginHotReloader(manager, fs);
        return (reloader, fs, manager);
    }

    [Fact]
    public async Task HandleStartWatching_CreatesWatcher_SetsIsWatching() {
        var (reloader, fs, _) = CreateReloader();
        await using var _ = reloader;
        fs.CreateDirectory("/plugins");
        var tcs = new TaskCompletionSource();
        var cmd = new StartWatchingCmd("/plugins", CancellationToken.None, tcs);

        await reloader.HandleStartWatching(cmd);

        reloader.IsWatching.Should().BeTrue();
        tcs.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task HandleStartWatching_AlreadyWatching_ShortCircuits_TrySetResult() {
        var (reloader, fs, _) = CreateReloader();
        await using var _ = reloader;
        fs.CreateDirectory("/plugins");
        var tcs1 = new TaskCompletionSource();
        await reloader.HandleStartWatching(new StartWatchingCmd("/plugins", CancellationToken.None, tcs1));

        var tcs2 = new TaskCompletionSource();
        await reloader.HandleStartWatching(new StartWatchingCmd("/plugins", CancellationToken.None, tcs2));

        // 已监控时短路:直接 TrySetResult,不重新创建 watcher
        tcs2.Task.IsCompleted.Should().BeTrue();
        reloader.IsWatching.Should().BeTrue();
    }

    [Fact]
    public async Task HandleStopWatching_AfterStart_StopsWatching() {
        var (reloader, fs, _) = CreateReloader();
        await using var _ = reloader;
        fs.CreateDirectory("/plugins");
        var startTcs = new TaskCompletionSource();
        await reloader.HandleStartWatching(new StartWatchingCmd("/plugins", CancellationToken.None, startTcs));

        var stopTcs = new TaskCompletionSource();
        await reloader.HandleStopWatching(new StopWatchingCmd(CancellationToken.None, stopTcs));

        reloader.IsWatching.Should().BeFalse();
        stopTcs.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task HandleReload_NotLoaded_FileNotExists_OnlyFiresEvents() {
        var (reloader, fs, _) = CreateReloader();
        await using var _ = reloader;
        PluginReloadEventArgs? reloadingArgs = null;
        PluginReloadEventArgs? reloadedArgs = null;
        reloader.PluginReloading += (_, e) => reloadingArgs = e;
        reloader.PluginReloaded += (_, e) => reloadedArgs = e;

        var cmd = new ReloadPluginCmd("nonexistent", "/plugins/missing.dll", ReloadReason.FileChanged);
        await reloader.HandleReload(cmd);

        reloadingArgs.Should().NotBeNull();
        reloadingArgs!.PluginName.Should().Be("nonexistent");
        reloadingArgs.Reason.Should().Be(ReloadReason.FileChanged);
        reloadedArgs.Should().NotBeNull();
        reloadedArgs!.PluginName.Should().Be("nonexistent");
    }

    [Fact]
    public async Task HandleReloadAndWait_NotLoaded_FileNotExists_TcsCompletes() {
        var (reloader, fs, _) = CreateReloader();
        await using var _ = reloader;
        var tcs = new TaskCompletionSource();
        var cmd = new ReloadPluginAndWaitCmd("nonexistent", "/plugins/missing.dll", ReloadReason.FileChanged, tcs);

        await reloader.HandleReloadAndWait(cmd);

        tcs.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ReloadPluginCoreAsync_NotLoaded_DoesNotThrow() {
        var (reloader, _, _) = CreateReloader();
        await using var _ = reloader;

        Func<Task> act = () => reloader.ReloadPluginCoreAsync("nonexistent", "/missing.dll", ReloadReason.FileChanged);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReloadPluginCoreAsync_FiresReloadingThenReloaded() {
        var (reloader, _, _) = CreateReloader();
        await using var _ = reloader;
        var order = new List<string>();
        reloader.PluginReloading += (_, _) => order.Add("reloading");
        reloader.PluginReloaded += (_, _) => order.Add("reloaded");

        await reloader.ReloadPluginCoreAsync("p", "/missing.dll", ReloadReason.FileChanged);

        order.Should().Equal(["reloading", "reloaded"]);
    }

    [Fact]
    public async Task ReloadPluginCoreAsync_FileDeleted_DoesNotAttemptLoad() {
        var (reloader, fs, _) = CreateReloader();
        await using var _ = reloader;
        await fs.WriteAllText("/plugins/deleted.dll", "content");
        fs.DeleteFile("/plugins/deleted.dll");

        Func<Task> act = () => reloader.ReloadPluginCoreAsync("deleted", "/plugins/deleted.dll", ReloadReason.FileDeleted);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReloadPluginCoreAsync_ManualReason_FiresEventsWithManualReason() {
        var (reloader, _, _) = CreateReloader();
        await using var _ = reloader;
        PluginReloadEventArgs? reloadedArgs = null;
        reloader.PluginReloaded += (_, e) => reloadedArgs = e;

        await reloader.ReloadPluginCoreAsync("manual-plugin", "/missing.dll", ReloadReason.Manual);

        reloadedArgs.Should().NotBeNull();
        reloadedArgs!.Reason.Should().Be(ReloadReason.Manual);
    }

    [Fact]
    public async Task PluginReloading_SubscriberThrows_DoesNotPropagate() {
        var (reloader, _, _) = CreateReloader();
        await using var _ = reloader;
        reloader.PluginReloading += (_, _) => throw new InvalidOperationException("subscriber boom");
        PluginReloadEventArgs? reloadedArgs = null;
        reloader.PluginReloaded += (_, e) => reloadedArgs = e;

        Func<Task> act = () => reloader.ReloadPluginCoreAsync("p", "/missing.dll", ReloadReason.FileChanged);

        // 订阅者异常应被隔离,不影响重载流程
        await act.Should().NotThrowAsync();
        reloadedArgs.Should().NotBeNull();
    }
}
