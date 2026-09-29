namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// PluginManager.UnloadAllPluginsCoreAsync mock 测试
/// <para>确定性测试:不依赖 Actor mailbox/时序/真实IO,直接调用 internal 全流程方法</para>
/// <para>验证空集合/顺序卸载/异常隔离(部分失败继续卸载)</para>
/// </summary>
public sealed class PluginManagerUnloadAllTest {
    private static PluginManager CreateManager() => new(new InMemoryFileSystem());

    [Fact]
    public async Task Empty_ReturnsEmpty() {
        var manager = CreateManager();
        await using var _ = manager;

        var results = await manager.UnloadAllPluginsCoreAsync(CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SingleWorkflow_UnloadsSuccessfully() {
        var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("single");
        await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        var results = await manager.UnloadAllPluginsCoreAsync(CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeTrue();
        results[0].PluginName.Should().Be("single");
        manager.IsPluginLoaded("single").Should().BeFalse();
    }

    [Fact]
    public async Task MultipleWorkflows_UnloadsInReverseLoadOrder() {
        var manager = CreateManager();
        await using var _ = manager;
        var plugin1 = new StubWorkflowPlugin("w1");
        var plugin2 = new StubWorkflowPlugin("w2");
        var plugin3 = new StubWorkflowPlugin("w3");
        await manager.LoadWorkflowPluginCoreAsync(plugin1, CancellationToken.None);
        await manager.LoadWorkflowPluginCoreAsync(plugin2, CancellationToken.None);
        await manager.LoadWorkflowPluginCoreAsync(plugin3, CancellationToken.None);
        var unloadOrder = new List<string>();
        manager.PluginUnloading += (_, name) => unloadOrder.Add(name);

        var results = await manager.UnloadAllPluginsCoreAsync(CancellationToken.None);

        results.Should().HaveCount(3);
        // 加载顺序 w1, w2, w3 → 逆序卸载 w3, w2, w1
        unloadOrder.Should().Equal(["w3", "w2", "w1"]);
        manager.LoadedWorkflowPluginNames.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleWorkflows_AllFibersTransitionToUnloaded() {
        var manager = CreateManager();
        await using var _ = manager;
        var plugin1 = new StubWorkflowPlugin("f1");
        var plugin2 = new StubWorkflowPlugin("f2");
        await manager.LoadWorkflowPluginCoreAsync(plugin1, CancellationToken.None);
        await manager.LoadWorkflowPluginCoreAsync(plugin2, CancellationToken.None);

        await manager.UnloadAllPluginsCoreAsync(CancellationToken.None);

        plugin1.Fiber.State.Should().Be(PluginFiberState.Unloaded);
        plugin2.Fiber.State.Should().Be(PluginFiberState.Unloaded);
    }

    [Fact]
    public async Task PartialFailure_ContinuesUnloadingOthers() {
        var manager = CreateManager();
        await using var _ = manager;
        var good1 = new StubWorkflowPlugin("good-1");
        var bad = new StubWorkflowPluginThrowOnUnload("bad") { ThrowOnUnload = true };
        var good2 = new StubWorkflowPlugin("good-2");
        await manager.LoadWorkflowPluginCoreAsync(good1, CancellationToken.None);
        await manager.LoadWorkflowPluginCoreAsync(bad, CancellationToken.None);
        await manager.LoadWorkflowPluginCoreAsync(good2, CancellationToken.None);

        var results = await manager.UnloadAllPluginsCoreAsync(CancellationToken.None);

        // 3 个插件都应被处理(异常隔离)
        results.Should().HaveCount(3);
        manager.LoadedWorkflowPluginNames.Should().BeEmpty();
        // good 插件应成功卸载
        good1.Fiber.State.Should().Be(PluginFiberState.Unloaded);
        good2.Fiber.State.Should().Be(PluginFiberState.Unloaded);
    }

    [Fact]
    public async Task AfterUnloadAll_CanReloadSamePlugins() {
        var manager = CreateManager();
        await using var _ = manager;
        var plugin1 = new StubWorkflowPlugin("reload-1");
        await manager.LoadWorkflowPluginCoreAsync(plugin1, CancellationToken.None);
        await manager.UnloadAllPluginsCoreAsync(CancellationToken.None);

        var plugin2 = new StubWorkflowPlugin("reload-1");
        var host = await manager.LoadWorkflowPluginCoreAsync(plugin2, CancellationToken.None);

        host.Should().NotBeNull();
        manager.IsPluginLoaded("reload-1").Should().BeTrue();
    }

    [Fact]
    public async Task UnloadAll_WithAsyncUndoChain_ExecutesInReverse() {
        var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("undo-all") { RegisterAsyncSideEffects = true };
        await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        await manager.UnloadAllPluginsCoreAsync(CancellationToken.None);

        plugin.AsyncDisposeOrder.Should().Equal(["third", "second", "first"]);
    }
}
