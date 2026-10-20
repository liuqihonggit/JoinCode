namespace JoinCode.Host.Tests.Engine;

/// <summary>
/// MCP 插件后台加载协调器测试（启动开销优化 — TASK037 后续）：
/// StartAsync 立即返回（启动不等八插件加载墙钟 — 实测 McpInitModule 并行 WhenAll 1.6s 阻塞启动）、
/// 加载链完成后调用就绪钩子（toolsBridge 同步 + kernel.Plugins 刷新 — 工具秒级自动补齐）、
/// 加载链/钩子异常全部吞掉（SafeAsync — 后台 Task 不 faulted 逃逸 ThreadPool）。
/// </summary>
public sealed class McpPluginBootstrapTests
{
    /// <summary>StartAsync — 立即返回：插件加载未完成（TCS 门闩未放行时后台链挂起、钩子未调）；
    /// 门闩放行后后台链完成、钩子被调。</summary>
    [Fact]
    public async Task StartAsync_ReturnsBeforePluginsComplete_ThenInvokesHookWhenDone()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hookInvoked = false;
        var background = McpPluginBootstrap.StartAsync(
            pluginLoads: _ => [gate.Task],
            onAllLoaded: async _ =>
            {
                hookInvoked = true;
                await Task.Yield();
            });

        await Task.Delay(50);
        Assert.False(background.IsCompleted);
        Assert.False(hookInvoked);

        gate.SetResult();
        await background.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(hookInvoked);
    }

    /// <summary>并行加载语义 — 全部加载完成后钩子才调用（WhenAll 墙钟语义保持）。</summary>
    [Fact]
    public async Task StartAsync_WaitsAllLoads_BeforeHook()
    {
        var gateA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = 0;
        var background = McpPluginBootstrap.StartAsync(
            pluginLoads: _ => [gateA.Task, gateB.Task],
            onAllLoaded: _ =>
            {
                Interlocked.Increment(ref observed);
                return Task.CompletedTask;
            });

        gateA.SetResult();
        await Task.Delay(50);
        Assert.False(background.IsCompleted);
        Assert.Equal(0, observed);

        gateB.SetResult();
        await background.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, observed);
    }

    /// <summary>加载链异常 — 吞掉不逃逸（后台 Task 成功完成语义，钩子不调）。</summary>
    [Fact]
    public async Task StartAsync_PluginLoadFault_SwallowsWithoutHook()
    {
        var hookInvoked = false;
        var background = McpPluginBootstrap.StartAsync(
            pluginLoads: _ => [Task.FromException(new InvalidOperationException("插件加载失败"))],
            onAllLoaded: _ =>
            {
                hookInvoked = true;
                return Task.CompletedTask;
            });

        await background.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TaskStatus.RanToCompletion, background.Status);
        Assert.False(hookInvoked);
    }

    /// <summary>钩子异常 — 同样吞掉不逃逸（后台 Task 成功完成语义）。</summary>
    [Fact]
    public async Task StartAsync_HookFault_Swallows()
    {
        var background = McpPluginBootstrap.StartAsync(
            pluginLoads: _ => [Task.CompletedTask],
            onAllLoaded: _ => Task.FromException(new InvalidOperationException("刷新失败")));

        await background.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TaskStatus.RanToCompletion, background.Status);
    }

    /// <summary>混合结果 — 部分成功部分失败（SafeAsync 各自吞）：钩子不调（WhenAll 非全成功）。</summary>
    [Fact]
    public async Task StartAsync_MixedLoadResults_SwallowsWithoutHook()
    {
        var hookInvoked = false;
        var background = McpPluginBootstrap.StartAsync(
            pluginLoads: _ => [Task.CompletedTask, Task.FromException(new InvalidOperationException("失败"))],
            onAllLoaded: _ =>
            {
                hookInvoked = true;
                return Task.CompletedTask;
            });

        await background.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TaskStatus.RanToCompletion, background.Status);
        Assert.False(hookInvoked);
    }

    /// <summary>宿主已取消 — 全部加载完成也不调就绪钩子（Dispose 竞态防护：
    /// LoadXxxSafeAsync 吞掉取消视为完成时，钩子前取消短路防碰已释放 IServiceProvider）。</summary>
    [Fact]
    public async Task StartAsync_CancelledToken_SkipsReadyHook()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var hookInvoked = false;
        var background = McpPluginBootstrap.StartAsync(
            pluginLoads: _ => [Task.CompletedTask],
            onAllLoaded: _ =>
            {
                hookInvoked = true;
                return Task.CompletedTask;
            },
            cts.Token);

        await background.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TaskStatus.RanToCompletion, background.Status);
        Assert.False(hookInvoked);
    }
}
