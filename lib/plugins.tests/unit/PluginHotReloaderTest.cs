namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// PluginHotReloader 单元测试 — 验证拆分出的 internal 子方法
/// <para>确定性测试:不依赖时序/Actor mailbox,直接调用 internal 方法</para>
/// <para>依赖 IO 的分支(StartWatching/Reload)留到阶段3</para>
/// </summary>
public sealed class PluginHotReloaderTest {
    private static (PluginHotReloader reloader, IFileSystem fs) CreateReloader() {
        var fs = new InMemoryFileSystem();
        var pluginManager = new PluginManager(fs);
        var reloader = new PluginHotReloader(pluginManager, fs);
        return (reloader, fs);
    }

    [Fact]
    public async Task HandleStopWatching_NotWatching_ShortCircuits_TrySetResultWithoutDispose() {
        var (reloader, _) = CreateReloader();
        await using var _ = reloader;
        var tcs = new TaskCompletionSource();
        var cmd = new StopWatchingCmd(CancellationToken.None, tcs);

        await reloader.HandleStopWatching(cmd);

        // 未监控时短路:_isWatchingInt 保持 0,Tcs 完成
        reloader.IsWatching.Should().BeFalse();
        tcs.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task HandleStopWatching_NotWatching_DoesNotThrowWhenWatcherNull() {
        // 默认状态:_watcher=null, _isWatchingInt=0,短路路径不应访问 _watcher
        var (reloader, _) = CreateReloader();
        await using var _ = reloader;
        var tcs = new TaskCompletionSource();
        var cmd = new StopWatchingCmd(CancellationToken.None, tcs);

        Func<Task> act = () => reloader.HandleStopWatching(cmd).AsTask();

        await act.Should().NotThrowAsync();
    }
}
