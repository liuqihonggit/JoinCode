namespace JoinCode.Host.Tests.Engine;

/// <summary>
/// AsyncHostWrapper 释放顺序测试（引擎侧后台插件链退出竞态修复）：
/// DisposeAsync 必须先收尾 McpPluginBootstrapHandle（取消 + 等后台链完成）再释放内层 Host——
/// 容器本体 dispose 先于后台链收尾时，插件 InitializeAsync 内 GetRequiredService 撞
/// ObjectDisposedException（Host --selftest 实测 2 条 fail 噪音根因）。
/// 顺序保证使所有宿主（CLI/GUI/TUI-Host）统一在容器存活期内完成后台链取消。
/// 判定方式：fake Host 释放时刻检查后台链门闩已放行（Handle 收尾完成 → 取消已传播且链已断）。
/// </summary>
public sealed class AsyncHostWrapperShutdownOrderTests
{
    /// <summary>构造包装宿主：fake IHost（释放时刻探测门闩）+ DI 注册后台句柄（Run 挂门闩后台链）。</summary>
    private static (AsyncHostWrapper Wrapper, ProbingHost Host, McpPluginBootstrapHandle Handle, TaskCompletionSource Gate) Create()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton<McpPluginBootstrapHandle>();
        var provider = services.BuildServiceProvider();
        var host = new ProbingHost(provider, gate);
        var wrapper = new AsyncHostWrapper(host);
        var handle = provider.GetRequiredService<McpPluginBootstrapHandle>();
        handle.Run(async ct =>
        {
            await using var registration = ct.Register(() => gate.TrySetResult());
            await gate.Task;
        });
        return (wrapper, host, handle, gate);
    }

    /// <summary>释放顺序 — Handle 收尾（门闩放行）先于内层 Host 释放（host 释放时刻门闩已完成）。</summary>
    [Fact]
    public async Task ShutdownAsync_CollapsesBootstrapHandle_BeforeHostDisposal()
    {
        var (wrapper, host, _, gate) = Create();
        Assert.False(gate.Task.IsCompleted);

        await wrapper.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(host.Disposed);
        Assert.True(host.GateCompletedAtDisposal, "Host 释放时刻后台链尚未收尾 — Handle 收尾必须先于容器释放");
    }

    /// <summary>取消传播 — DisposeAsync 触发 Handle 取消令牌（后台链据此断链收尾）。</summary>
    [Fact]
    public async Task ShutdownAsync_CancelsHandleToken()
    {
        var (wrapper, _, _, gate) = Create();

        await wrapper.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(gate.Task.IsCompleted);
    }

    /// <summary>无 Handle 注册（模块链不含 McpInitModule 的极端组装）— DisposeAsync no-op 直达宿主释放不抛。</summary>
    [Fact]
    public async Task ShutdownAsync_WithoutRegisteredHandle_DisposesHost()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var host = new ProbingHost(provider, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var wrapper = new AsyncHostWrapper(host);

        await wrapper.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(host.Disposed);
    }

    /// <summary>内层 Host 非 IAsyncDisposable — 回退同步 Dispose 路径同样先收尾 Handle。</summary>
    [Fact]
    public async Task ShutdownAsync_SyncOnlyHost_StillCollapsesHandleFirst()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton<McpPluginBootstrapHandle>();
        var provider = services.BuildServiceProvider();
        var host = new SyncOnlyHost(provider, gate);
        var wrapper = new AsyncHostWrapper(host);
        var handle = provider.GetRequiredService<McpPluginBootstrapHandle>();
        handle.Run(async ct =>
        {
            await using var registration = ct.Register(() => gate.TrySetResult());
            await gate.Task;
        });

        await wrapper.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(host.Disposed);
        Assert.True(host.GateCompletedAtDisposal, "同步回退路径同样必须先收尾 Handle 再释放容器");
    }

    /// <summary>释放时刻探测门闩的 fake IHost（IAsyncDisposable）。</summary>
    private sealed class ProbingHost(IServiceProvider services, TaskCompletionSource gate) : IHost, IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        /// <summary>释放时刻后台链门闩是否已完成（顺序判定面）。</summary>
        public bool GateCompletedAtDisposal { get; private set; }

        public IServiceProvider Services => services;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
            GateCompletedAtDisposal = gate.Task.IsCompleted;
            Disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>仅同步 Dispose 的 fake IHost（回退路径）。</summary>
    private sealed class SyncOnlyHost(IServiceProvider services, TaskCompletionSource gate) : IHost
    {
        public bool Disposed { get; private set; }

        public bool GateCompletedAtDisposal { get; private set; }

        public IServiceProvider Services => services;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
            GateCompletedAtDisposal = gate.Task.IsCompleted;
            Disposed = true;
        }
    }
}
