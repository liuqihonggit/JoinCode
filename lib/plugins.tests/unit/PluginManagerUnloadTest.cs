namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// PluginManager.UnloadPluginCoreAsync 三分支 mock 测试
/// <para>确定性测试:不依赖 Actor mailbox/时序/真实IO,直接调用 internal 全流程方法</para>
/// <para>Workflow 分支可构造 stub host;External/Native 依赖真实 Process/DLL 留到 E2E</para>
/// </summary>
public sealed class PluginManagerUnloadTest {
    private static PluginManager CreateManager() => new(new InMemoryFileSystem());

    [Fact]
    public async Task NotLoaded_ReturnsAlreadyUnloaded() {
        using var manager = CreateManager();
        await using var _ = manager;

        var result = await manager.UnloadPluginCoreAsync("not-loaded", CancellationToken.None);

        result.Status.Should().Be(PluginUnloadStatus.AlreadyUnloaded);
        result.PluginName.Should().Be("not-loaded");
    }

    [Fact]
    public async Task WorkflowPlugin_RemovesFromRegistry_AndReturnsSuccess() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("unload-success");
        await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);
        manager.IsPluginLoaded("unload-success").Should().BeTrue();

        var result = await manager.UnloadPluginCoreAsync("unload-success", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.PluginName.Should().Be("unload-success");
        manager.IsPluginLoaded("unload-success").Should().BeFalse();
    }

    [Fact]
    public async Task WorkflowPlugin_FiberTransitionsToUnloaded() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("fiber-unload");
        await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);
        plugin.Fiber.State.Should().Be(PluginFiberState.Active);

        await manager.UnloadPluginCoreAsync("fiber-unload", CancellationToken.None);

        plugin.Fiber.State.Should().Be(PluginFiberState.Unloaded);
    }

    [Fact]
    public async Task WorkflowPlugin_AsyncUndoChainExecutedInReverseOrder() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("undo-chain") { RegisterAsyncSideEffects = true };
        await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        await manager.UnloadPluginCoreAsync("undo-chain", CancellationToken.None);

        // 注册顺序: first, second, third → 逆序执行: third, second, first
        plugin.AsyncDisposeOrder.Should().Equal(["third", "second", "first"]);
    }

    [Fact]
    public async Task WorkflowPlugin_PluginUnloadingEventFires() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("event-unload");
        await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);
        string? unloadingName = null;
        manager.PluginUnloading += (_, name) => unloadingName = name;

        await manager.UnloadPluginCoreAsync("event-unload", CancellationToken.None);

        unloadingName.Should().Be("event-unload");
    }

    [Fact]
    public async Task WorkflowPlugin_DoubleUnload_SecondReturnsAlreadyUnloaded() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("double-unload");
        await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        var first = await manager.UnloadPluginCoreAsync("double-unload", CancellationToken.None);
        var second = await manager.UnloadPluginCoreAsync("double-unload", CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.Status.Should().Be(PluginUnloadStatus.AlreadyUnloaded);
    }

    [Fact]
    public async Task CascadeUnload_DependentUnloadedBeforeDepended() {
        using var manager = CreateManager();
        await using var _ = manager;
        var basePlugin = new StubWorkflowPlugin("base-plugin");
        var depPlugin = new StubWorkflowPluginWithDependencies("dep-plugin", "base-plugin");
        await manager.LoadWorkflowPluginCoreAsync(basePlugin, CancellationToken.None);
        await manager.LoadWorkflowPluginCoreAsync(depPlugin, CancellationToken.None);
        var unloadOrder = new List<string>();
        manager.PluginUnloading += (_, name) => unloadOrder.Add(name);

        // 卸载 base-plugin → dep-plugin 应先被连带卸载
        await manager.UnloadPluginCoreAsync("base-plugin", CancellationToken.None);

        unloadOrder.Should().Equal(["dep-plugin", "base-plugin"]);
        manager.IsPluginLoaded("base-plugin").Should().BeFalse();
        manager.IsPluginLoaded("dep-plugin").Should().BeFalse();
    }

    [Fact]
    public async Task UnloadWorkflowPlugin_AfterLoad_GetWorkflowPluginReturnsNull() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("get-null");
        await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);
        manager.GetWorkflowPlugin("get-null").Should().NotBeNull();

        await manager.UnloadPluginCoreAsync("get-null", CancellationToken.None);

        manager.GetWorkflowPlugin("get-null").Should().BeNull();
    }

    [Fact]
    public async Task UnloadPlugin_LoadedWorkflow_ResourcesReleased() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("resource-release");
        await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);
        plugin.IsAlive.Should().BeTrue();

        await manager.UnloadPluginCoreAsync("resource-release", CancellationToken.None);

        // WorkflowPluginBase.UnloadAsync 会 MarkDead
        plugin.IsAlive.Should().BeFalse();
    }
}
