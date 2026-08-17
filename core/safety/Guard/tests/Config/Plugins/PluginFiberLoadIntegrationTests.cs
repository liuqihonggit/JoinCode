namespace Core.Tests.Plugins;

/// <summary>
/// 断裂点1 测试: Fiber 加载状态集成到 PluginManager.LoadWorkflowPluginAsync
/// 验证 Load → Loading → Active, 失败 → Failed
/// </summary>
public sealed class PluginFiberLoadIntegrationTests
{
    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IPluginManager, PluginManager>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task LoadWorkflowPluginAsync_Success_FiberTransitionsToActive()
    {
        var sp = CreateServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var host = await pm.LoadWorkflowPluginAsync<FiberActiveTestPlugin>().ConfigureAwait(true);

        var plugin = (FiberActiveTestPlugin)host.Plugin;
        plugin.Fiber.State.Should().Be(PluginFiberState.Active);

        await pm.UnloadPluginAsync(plugin.Name).ConfigureAwait(true);
        sp.Dispose();
    }

    [Fact]
    public async Task LoadWorkflowPluginAsync_LoadFails_ThrowsInf032()
    {
        var sp = CreateServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var act = async () => await pm.LoadWorkflowPluginAsync<FiberLoadFailTestPlugin>().ConfigureAwait(true);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true))
            .WithMessage("[INF032]*");

        sp.Dispose();
    }

    [Fact]
    public async Task LoadWorkflowPluginAsync_InitializeFails_ThrowsInf033()
    {
        var sp = CreateServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var act = async () => await pm.LoadWorkflowPluginAsync<FiberInitFailTestPlugin>().ConfigureAwait(true);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true))
            .WithMessage("[INF033]*");

        sp.Dispose();
    }

    [Fact]
    public async Task LoadWorkflowPluginAsync_ContractViolation_ThrowsContractError()
    {
        var sp = CreateServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var act = async () => await pm.LoadWorkflowPluginAsync<FiberContractFailTestPlugin>().ConfigureAwait(true);

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true))
            .WithMessage("[INF-PLUGIN-CONTRACT]*");

        sp.Dispose();
    }

    [Fact]
    public async Task UnloadPluginAsync_AfterSuccessfulLoad_FiberTransitionsToDisposed()
    {
        var sp = CreateServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var host = await pm.LoadWorkflowPluginAsync<FiberActiveTestPlugin2>().ConfigureAwait(true);
        var plugin = (FiberActiveTestPlugin2)host.Plugin;
        plugin.Fiber.State.Should().Be(PluginFiberState.Active);

        await pm.UnloadPluginAsync(plugin.Name).ConfigureAwait(true);
        plugin.Fiber.State.Should().Be(PluginFiberState.Disposed);
        sp.Dispose();
    }

    #region Test Plugins

    private sealed class FiberActiveTestPlugin : WorkflowPluginBase
    {
        public FiberActiveTestPlugin() : base("fiber-active-test") { }
        public override string Name => "fiber-active-test";
        public override string Version => "1.0.0";
        public override string Description => "Fiber Active test";
        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
    }

    private sealed class FiberActiveTestPlugin2 : WorkflowPluginBase
    {
        public FiberActiveTestPlugin2() : base("fiber-active-test-2") { }
        public override string Name => "fiber-active-test-2";
        public override string Version => "1.0.0";
        public override string Description => "Fiber Active test 2";
        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
    }

    private sealed class FiberLoadFailTestPlugin : WorkflowPluginBase
    {
        public FiberLoadFailTestPlugin() : base("fiber-load-fail-test") { }
        public override string Name => "fiber-load-fail-test";
        public override string Version => "1.0.0";
        public override string Description => "Fiber Load fail test";
        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Fail("Load failed"));
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
    }

    private sealed class FiberInitFailTestPlugin : WorkflowPluginBase
    {
        public FiberInitFailTestPlugin() : base("fiber-init-fail-test") { }
        public override string Name => "fiber-init-fail-test";
        public override string Version => "1.0.0";
        public override string Description => "Fiber Init fail test";
        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Fail("Init failed"));
    }

    private sealed class FiberContractFailTestPlugin : WorkflowPluginBase
    {
        public FiberContractFailTestPlugin() : base("fiber-contract-fail-test") { }
        public override string Name => "fiber-contract-fail-test";
        public override string Version => "1.0.0";
        public override string Description => "Fiber Contract fail test";
        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
        public override PluginUnloadContract ValidateUnloadContract()
            => PluginUnloadContract.Invalid("No disposer registered");
    }

    #endregion
}
