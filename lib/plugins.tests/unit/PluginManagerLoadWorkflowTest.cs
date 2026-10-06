namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// PluginManager.LoadWorkflowPluginCoreAsync 全流程 mock 测试
/// <para>确定性测试:不依赖 Actor mailbox/时序/真实IO,直接调用 internal 全流程方法</para>
/// <para>给定 stub IWorkflowPlugin + InMemoryFileSystem → 断言加载结果/Fiber 状态/注册表</para>
/// </summary>
public sealed class PluginManagerLoadWorkflowTest {
    private static PluginManager CreateManager() => new(new InMemoryFileSystem());

    [Fact]
    public async Task Success_AddsToRegistry_AndFiberActive() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("test-plugin");

        using var host = await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        host.Should().NotBeNull();
        host.PluginName.Should().Be("test-plugin");
        host.PluginType.Should().Be(PluginKind.Workflow);
        manager.IsPluginLoaded("test-plugin").Should().BeTrue();
        manager.LoadedWorkflowPluginNames.Should().Contain("test-plugin");
        plugin.Fiber.State.Should().Be(PluginFiberState.Active);
        plugin.LoadCallCount.Should().Be(1);
        plugin.InitCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Success_ReturnsHostWithPluginInstance() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("instance-plugin");

        using var host = await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        host.Plugin.Should().BeSameAs(plugin);
        host.Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task DuplicateLoad_ThrowsInvalidOperationException_AndKeepsFirstLoad() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin1 = new StubWorkflowPlugin("dup-plugin");
        await manager.LoadWorkflowPluginCoreAsync(plugin1, CancellationToken.None);

        var plugin2 = new StubWorkflowPlugin("dup-plugin");
        Func<Task> act = () => manager.LoadWorkflowPluginCoreAsync(plugin2, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dup-plugin*");
        manager.IsPluginLoaded("dup-plugin").Should().BeTrue();
        // CheckNotDuplicateLoad 在 TransitionFiberTo 之前抛异常,plugin2.Fiber 保持初始 Pending
        plugin2.Fiber.State.Should().Be(PluginFiberState.Pending);
    }

    [Fact]
    public async Task Blacklisted_ThrowsInvalidOperationException_AndDoesNotLoad() {
        using var manager = CreateManager();
        await using var _ = manager;
        manager.AddToBlacklistForTest("bad-plugin");
        var plugin = new StubWorkflowPlugin("bad-plugin");

        Func<Task> act = () => manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*bad-plugin*");
        manager.IsPluginLoaded("bad-plugin").Should().BeFalse();
        plugin.LoadCallCount.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsyncReturnsFail_FiberBecomesFailed_AndNotRegistered() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("load-fail") {
            LoadResult = OperationResult.Fail("load error")
        };

        Func<Task> act = () => manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*load-fail*");
        plugin.Fiber.State.Should().Be(PluginFiberState.Failed);
        manager.IsPluginLoaded("load-fail").Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsyncReturnsFail_FiberBecomesUnloaded_AndNotRegistered() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("init-fail") {
            InitResult = OperationResult.Fail("init error")
        };

        Func<Task> act = () => manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*init-fail*");
        // TransitionFiberTo(Failed) 后调用 host.UnloadAsync 把 Fiber 从 Failed 转到 Unloaded
        plugin.Fiber.State.Should().Be(PluginFiberState.Unloaded);
        manager.IsPluginLoaded("init-fail").Should().BeFalse();
        plugin.LoadCallCount.Should().Be(1);
        plugin.InitCallCount.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsyncThrows_FiberBecomesFailed_AndNotRegistered() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("throw-plugin") {
            LoadException = new NotSupportedException("boom")
        };

        Func<Task> act = () => manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        // host.LoadAsync catch 异常返回 Fail → LoadWorkflowPluginCoreAsync 走 !Success 分支
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*throw-plugin*");
        plugin.Fiber.State.Should().Be(PluginFiberState.Failed);
        manager.IsPluginLoaded("throw-plugin").Should().BeFalse();
    }

    [Fact]
    public async Task ContractViolation_FiberBecomesUnloaded_AndNotRegistered() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPluginWithBadContract("contract-violation");

        Func<Task> act = () => manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*contract-violation*");
        // TransitionTo(Failed) 后调用 host.UnloadAsync 把 Fiber 从 Failed 转到 Unloaded
        plugin.Fiber.State.Should().Be(PluginFiberState.Unloaded);
        manager.IsPluginLoaded("contract-violation").Should().BeFalse();
    }

    [Fact]
    public async Task CancellationToken_Cancelled_PluginRespectsIt_LoadFails() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("cancel-plugin") { RespectCancellation = true };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => manager.LoadWorkflowPluginCoreAsync(plugin, cts.Token);

        // 修复后:plugin.LoadAsync 抛 OperationCanceledException → host.LoadAsync 透传 OCE
        // → LoadWorkflowPluginCoreAsync catch (not InvalidOperationException) → rethrow OCE
        await act.Should().ThrowAsync<OperationCanceledException>();
        plugin.Fiber.State.Should().Be(PluginFiberState.Failed);
        manager.IsPluginLoaded("cancel-plugin").Should().BeFalse();
    }

    [Fact]
    public async Task CancellationToken_PassedToPluginLoadAsync() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("ct-pass");
        using var cts = new CancellationTokenSource();

        await manager.LoadWorkflowPluginCoreAsync(plugin, cts.Token);

        plugin.LastLoadCancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task PluginLoadedEvent_FiresWithPluginName() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPlugin("event-plugin");
        string? firedName = null;
        manager.PluginLoaded += (_, name) => firedName = name;

        await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        firedName.Should().Be("event-plugin");
    }

    [Fact]
    public async Task DependenciesDeclared_LoadSucceeds() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin = new StubWorkflowPluginWithDependencies("dep-plugin", "dep-a", "dep-b");

        using var host = await manager.LoadWorkflowPluginCoreAsync(plugin, CancellationToken.None);

        host.Should().NotBeNull();
        manager.IsPluginLoaded("dep-plugin").Should().BeTrue();
        plugin.Fiber.State.Should().Be(PluginFiberState.Active);
    }

    [Fact]
    public async Task MultipleDistinctPlugins_AllLoadSuccessfully() {
        using var manager = CreateManager();
        await using var _ = manager;
        var plugin1 = new StubWorkflowPlugin("multi-1");
        var plugin2 = new StubWorkflowPlugin("multi-2");
        var plugin3 = new StubWorkflowPlugin("multi-3");

        await manager.LoadWorkflowPluginCoreAsync(plugin1, CancellationToken.None);
        await manager.LoadWorkflowPluginCoreAsync(plugin2, CancellationToken.None);
        await manager.LoadWorkflowPluginCoreAsync(plugin3, CancellationToken.None);

        manager.LoadedWorkflowPluginNames.Should().Contain(["multi-1", "multi-2", "multi-3"]);
        plugin1.Fiber.State.Should().Be(PluginFiberState.Active);
        plugin2.Fiber.State.Should().Be(PluginFiberState.Active);
        plugin3.Fiber.State.Should().Be(PluginFiberState.Active);
    }
}
