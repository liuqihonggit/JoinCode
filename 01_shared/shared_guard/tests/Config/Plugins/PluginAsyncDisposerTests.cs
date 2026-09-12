namespace Core.Tests.Plugins;

public sealed class PluginAsyncDisposerTests
{
    [Fact]
    public async Task LoadAsync_WithAsyncEffect_DisposeAsyncExecutedOnUnload()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IPluginManager, PluginManager>();
        var sp = services.BuildServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var host = await pm.LoadWorkflowPluginAsync<AsyncEffectPlugin>().ConfigureAwait(true);
        var plugin = (AsyncEffectPlugin)host.Plugin;
        Assert.False(plugin.AsyncDisposed);

        await pm.UnloadPluginAsync("async-effect").ConfigureAwait(true);

        Assert.True(plugin.AsyncDisposed);
    }

    private sealed class AsyncEffectPlugin : WorkflowPluginBase
    {
        public bool AsyncDisposed { get; private set; }
        public AsyncEffectPlugin() : base("async-effect") { }
        public override string Name => "async-effect";
        public override string Version => "1.0.0";
        public override string Description => "async effect测试";
        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        {
            ctx.Effect(() => new AsyncDisposable(() => AsyncDisposed = true));
            return Task.FromResult(OperationResult.Ok());
        }
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
    }

    private sealed class AsyncDisposable : IAsyncDisposable
    {
        private readonly Action _onDispose;
        public AsyncDisposable(Action onDispose) => _onDispose = onDispose;
        public ValueTask DisposeAsync()
        {
            _onDispose();
            return ValueTask.CompletedTask;
        }
    }
}
