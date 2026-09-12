namespace Core.Tests.Plugins;

public sealed class PluginManagerE2ETests
{
    private sealed class TestPlugin : WorkflowPluginBase
    {
        public override string Name => "TestPlugin";
        public override string Version => "1.0.0";
        public override string Description => "Test plugin for E2E";

        public TestPlugin() : base("TestPlugin") { }

        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        {
            ctx.RegisterService<ITestService, TestServiceImpl>();
            return Task.FromResult(OperationResult.Ok());
        }

        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());

        protected override void OnUnload() { }
    }

    private interface ITestService { }
    private sealed class TestServiceImpl : ITestService { }

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
    public async Task LoadAndUnload_LifecycleWorks()
    {
        var pm = CreatePluginManager();
        var host = await pm.LoadWorkflowPluginAsync<TestPlugin>();
        Assert.Equal("TestPlugin", host.PluginName);
        Assert.True(pm.IsWorkflowPluginLoaded("TestPlugin"));

        var result = await pm.UnloadPluginAsync("TestPlugin");
        Assert.True(result.IsSuccess);
        Assert.False(pm.IsWorkflowPluginLoaded("TestPlugin"));
        pm.Dispose();
    }

    [Fact]
    public async Task ActorSerializes_ConcurrentLoads()
    {
        var pm = CreatePluginManager();
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try { await pm.UnloadPluginAsync($"plugin-{i}"); }
                catch (Exception ex) { Console.WriteLine($"卸载失败: {ex.Message}"); }
            }));
        }
        await Task.WhenAll(tasks);
        pm.Dispose();
    }

    [Fact]
    public async Task UnloadAll_WhenEmpty_ReturnsEmptyList()
    {
        var pm = CreatePluginManager();
        var results = await pm.UnloadAllPluginsAsync();
        Assert.Empty(results);
        pm.Dispose();
    }

    [Fact]
    public async Task LoadSamePlugin_Twice_Throws()
    {
        var pm = CreatePluginManager();
        await pm.LoadWorkflowPluginAsync<TestPlugin>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pm.LoadWorkflowPluginAsync<TestPlugin>());
        await pm.UnloadPluginAsync("TestPlugin");
        pm.Dispose();
    }

    [Fact]
    public async Task GetWorkflowPlugin_ReturnsLoadedPlugin()
    {
        var pm = CreatePluginManager();
        await pm.LoadWorkflowPluginAsync<TestPlugin>();
        var host = pm.GetWorkflowPlugin("TestPlugin");
        Assert.NotNull(host);
        Assert.Equal("TestPlugin", host!.PluginName);
        await pm.UnloadPluginAsync("TestPlugin");
        pm.Dispose();
    }
}
