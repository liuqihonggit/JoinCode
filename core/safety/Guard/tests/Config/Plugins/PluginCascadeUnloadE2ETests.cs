namespace Core.Tests.Plugins;

public sealed class PluginCascadeUnloadE2ETests
{
    private sealed class ProviderPlugin : WorkflowPluginBase
    {
        public override string Name => "ProviderPlugin";
        public override string Version => "1.0.0";
        public override string Description => "Provides a service";

        public ProviderPlugin() : base("ProviderPlugin") { }

        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        {
            ctx.RegisterService<ICascadeService, CascadeServiceImpl>();
            return Task.FromResult(OperationResult.Ok());
        }

        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());

        protected override void OnUnload() { }
    }

    private sealed class ConsumerPlugin : WorkflowPluginBase, IPluginDependencies
    {
        public override string Name => "ConsumerPlugin";
        public override string Version => "1.0.0";
        public override string Description => "Consumes a service from ProviderPlugin";
        public IReadOnlyList<string> Dependencies => new[] { "ProviderPlugin" };

        public ConsumerPlugin() : base("ConsumerPlugin") { }

        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());

        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());

        protected override void OnUnload() { }
    }

    private interface ICascadeService { }
    private sealed class CascadeServiceImpl : ICascadeService { }

    private static PluginManager CreatePluginManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        return new PluginManager(
            fs: new PhysicalFileSystem(),
            loggerFactory: sp.GetService<ILoggerFactory>(),
            logger: sp.GetService<ILogger<PluginManager>>(),
            serviceProvider: sp);
    }

    [Fact]
    public async Task UnloadProvider_CascadesConsumer()
    {
        var pm = CreatePluginManager();
        await pm.LoadWorkflowPluginAsync<ProviderPlugin>();
        await pm.LoadWorkflowPluginAsync<ConsumerPlugin>();

        Assert.True(pm.IsWorkflowPluginLoaded("ProviderPlugin"));
        Assert.True(pm.IsWorkflowPluginLoaded("ConsumerPlugin"));

        var result = await pm.UnloadPluginAsync("ProviderPlugin");
        Assert.True(result.IsSuccess);

        Assert.False(pm.IsWorkflowPluginLoaded("ProviderPlugin"),
            "ProviderPlugin 应被卸载");
        Assert.False(pm.IsWorkflowPluginLoaded("ConsumerPlugin"),
            "ConsumerPlugin 应被连带卸载");

        pm.Dispose();
    }

    [Fact]
    public async Task UnloadConsumer_DoesNotCascadeProvider()
    {
        var pm = CreatePluginManager();
        await pm.LoadWorkflowPluginAsync<ProviderPlugin>();
        await pm.LoadWorkflowPluginAsync<ConsumerPlugin>();

        var result = await pm.UnloadPluginAsync("ConsumerPlugin");
        Assert.True(result.IsSuccess);

        Assert.False(pm.IsWorkflowPluginLoaded("ConsumerPlugin"));
        Assert.True(pm.IsWorkflowPluginLoaded("ProviderPlugin"),
            "ProviderPlugin 不应被连带卸载(只有被依赖者卸载才连带)");

        await pm.UnloadPluginAsync("ProviderPlugin");
        pm.Dispose();
    }

    [Fact]
    public async Task LoadAfterCascadeUnload_Succeeds()
    {
        var pm = CreatePluginManager();
        await pm.LoadWorkflowPluginAsync<ProviderPlugin>();
        await pm.LoadWorkflowPluginAsync<ConsumerPlugin>();

        await pm.UnloadPluginAsync("ProviderPlugin");

        Assert.False(pm.IsWorkflowPluginLoaded("ProviderPlugin"));
        Assert.False(pm.IsWorkflowPluginLoaded("ConsumerPlugin"));

        var host = await pm.LoadWorkflowPluginAsync<ProviderPlugin>();
        Assert.NotNull(host);
        Assert.True(pm.IsWorkflowPluginLoaded("ProviderPlugin"));

        await pm.UnloadPluginAsync("ProviderPlugin");
        pm.Dispose();
    }
}
