namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// TASK136 C1 真机探针暴露的卸载缺陷：插件容器内有仅实现 <c>IAsyncDisposable</c> 的服务
/// （DreamFeature）时，<c>ServiceProvider.Dispose()</c> 抛
/// <c>InvalidOperationException: ... Use DisposeAsync to dispose the container</c>，
/// <c>WorkflowPluginHost.UnloadAsync</c> 因此被降级为卸载失败。
/// </summary>
public sealed class WorkflowPluginHostAsyncDisposeTests {
    /// <summary>异步专用服务 — 只有 DisposeAsync，没有 Dispose。</summary>
    private sealed class AsyncOnlyService : IAsyncDisposable {
        private readonly TaskCompletionSource _disposed;
        private int _disposeState;
        /// <summary>构造异步专用服务。</summary>
        public AsyncOnlyService(TaskCompletionSource disposed) => _disposed = disposed;
        /// <inheritdoc />
        public ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposeState, 1) == 0) {
                _disposed.TrySetResult();
            }
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>把异步专用服务注册进插件容器的测试插件。</summary>
    private sealed class AsyncServicePlugin(TaskCompletionSource disposed) : WorkflowPluginBase("async-service-plugin") {
        /// <summary>插件名。</summary>
        public override string Name => "async-service-plugin";
        /// <summary>插件版本。</summary>
        public override string Version => "1.0.0";
        /// <summary>插件描述。</summary>
        public override string Description => "test stub";
        /// <inheritdoc />
        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default) {
            ctx.ConfigureServices(services =>
                services.AddSingleton<IAsyncDisposable>(_ => new AsyncOnlyService(disposed)));
            return Task.FromResult(OperationResult.Ok());
        }
        /// <inheritdoc />
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
    }

    /// <summary>卸载必须 await 容器异步释放，不能同步 Dispose。</summary>
    [Fact]
    public async Task UnloadAsync_PluginContainerHasAsyncOnlyService_DisposesAsynchronously() {
        var disposed = new TaskCompletionSource();
        await using var host = new WorkflowPluginHost(new AsyncServicePlugin(disposed));

        (await host.LoadAsync().ConfigureAwait(true)).Success.Should().BeTrue();
        host.GetService<IAsyncDisposable>().Should().NotBeNull("容器必须先实际创建该单例，才会在释放时跟踪它");
        var result = await host.UnloadAsync().ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue("异步容器必须能正常卸载，而不是降级为 AlcUnloadFailed");
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
    }
}
