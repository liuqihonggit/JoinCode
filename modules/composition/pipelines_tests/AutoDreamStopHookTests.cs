namespace Pipelines.Tests;

/// <summary>
/// TASK126（TASK103 #40）— 回合结束钩子接线：
/// ① orders.yaml 必须登记它（否则 <c>OrderIndex.OrderOf</c> 直接抛，等于启动期 fail-fast）；
/// ② 无论 dream 是否可用，<c>OnStopAsync</c> 都必须返回 Continue 且不阻塞回合；
/// ③ 容器里拿到的钩子必须真的能投递（不是空壳）。
/// </summary>
public sealed class AutoDreamStopHookTests {
    private static Core.Query.StopHooks.StopHookContext Context() =>
        new() { SessionId = "session-1", Reason = "query-complete" };

    [Fact]
    public void Priority_ComesFromOrdersYaml() {
        var hook = new AutoDreamStopHook(null);

        // OrderIndex.OrderOf 对未登记的注册名直接抛异常：本断言同时是 orders.yaml 的 CI 门禁。
        hook.Priority.Should().BeGreaterThan(0);
        hook.Name.Should().Be("AutoDreamStopHook");
    }

    [Fact]
    public async Task NoTrigger_ReturnsContinue() {
        var hook = new AutoDreamStopHook(null);

        var result = await hook.OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true);

        result.ShouldStop.Should().BeFalse("没启用 dream 时不阻止回合，也不短路其它 hook");
    }

    /// <summary>
    /// Dream 插件由 <c>McpInitModule</c> 后台加载，首回合解析不到是常态（真机 `jcc -p hi` 实测）。
    /// 钩子必须「每回合重试、成功即缓存」：不重试则短会话永久降级；不缓存则每回合新建
    /// <c>AutoDreamTrigger</c>，扫描节流状态（<c>_lastScanAtMs</c>）等于被废掉。
    /// </summary>
    [Fact]
    public async Task DeferredResolution_RetriesUntilDreamReady_ThenCaches() {
        var fired = new TaskCompletionSource();
        var dream = new Mock<JoinCode.Dream.IDreamFeature>();
        dream.Setup(d => d.ExecuteAsync(It.IsAny<JoinCode.Dream.DreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns((JoinCode.Dream.DreamRequest request, CancellationToken token) => {
                fired.TrySetResult();
                return Task.FromResult(JoinCode.Dream.DreamResult.Success("ok", "task-1", 1, 5));
            });
        var scanner = new Mock<JoinCode.Dream.Services.ISessionScanner>();
        scanner.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["s1"]);

        var config = new JoinCode.Dream.AutoDreamConfig { MinHours = 0, MinSessions = 1, ProjectDir = "/proj-deferred" };
        var trigger = new JoinCode.Dream.AutoDreamTrigger(
            dream.Object,
            scanner.Object,
            config,
            new JoinCode.Dream.Services.ConsolidationLock(config, new Testing.Common.Services.InMemoryFileSystem()));

        var resolveCount = 0;
        var hook = new AutoDreamStopHook(() => {
            resolveCount++;
            return resolveCount <= 2 ? null : trigger;
        });

        await hook.OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true);
        await hook.OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true);
        resolveCount.Should().Be(2, "前两回合 dream 还没注册完成，必须重试而不是永久放弃");
        fired.Task.IsCompleted.Should().BeFalse("前两回合不该发起整合");

        await hook.OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true);
        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        await hook.OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true);
        resolveCount.Should().Be(3, "解析成功后必须缓存 —— AutoDreamTrigger 持有扫描节流状态");
        dream.Verify(d => d.ExecuteAsync(It.IsAny<JoinCode.Dream.DreamRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNotAwaitDream_ReturnsContinueWhileDreamStillRunning() {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var dream = new Mock<JoinCode.Dream.IDreamFeature>();
        dream.Setup(d => d.ExecuteAsync(It.IsAny<JoinCode.Dream.DreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (JoinCode.Dream.DreamRequest request, CancellationToken token) => {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(true);
                return JoinCode.Dream.DreamResult.Success("ok", "task-1", 1, 5);
            });
        var scanner = new Mock<JoinCode.Dream.Services.ISessionScanner>();
        scanner.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["s1"]);
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new JoinCode.Dream.AutoDreamConfig { MinHours = 0, MinSessions = 1, ProjectDir = "/proj" };
        var trigger = new JoinCode.Dream.AutoDreamTrigger(
            dream.Object,
            scanner.Object,
            config,
            new JoinCode.Dream.Services.ConsolidationLock(config, fs),
            NullLogger<JoinCode.Dream.AutoDreamTrigger>.Instance);
        var hook = new AutoDreamStopHook(() => trigger, NullLogger<AutoDreamStopHook>.Instance);

        var result = await hook.OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true);
        await entered.Task.ConfigureAwait(true);

        result.ShouldStop.Should().BeFalse("整合还在跑，回合已经该结束了 —— 这正是 fire-and-forget 的判据");
        release.TrySetResult();
    }

    /// <summary>
    /// 依赖解析期抛异常（例如 `IChatClient` 缺失导致 `ChatCompletionClient` 激活失败）时，
    /// 必须降级成永不触发 —— 这是查询链的构造路径，一个后台功能不能把回合拖垮。
    /// </summary>
    [Fact]
    public async Task DependencyActivationThrows_DegradesToDisabledHook() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new JoinCode.Dream.AutoDreamConfig());
        services.AddSingleton<JoinCode.Dream.IDreamFeature>(_ => throw new InvalidOperationException("kernel missing"));
        services.RegisterBaseMiddlewareServices();

        await using var sp = services.BuildServiceProvider();
        var hook = sp.GetServices<Core.Query.StopHooks.IQueryStopHook>().Single();

        var result = await hook.OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true);
        result.ShouldStop.Should().BeFalse();
    }

    [Fact]
    public async Task WithoutDreamSubsystem_RegistrationDegradesToDisabledHook() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.RegisterBaseMiddlewareServices();

        await using var sp = services.BuildServiceProvider();
        var hooks = sp.GetServices<Core.Query.StopHooks.IQueryStopHook>().ToList();

        hooks.Should().ContainSingle("只有一个 query stop hook 实现");
        var result = await hooks[0].OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true);
        result.ShouldStop.Should().BeFalse("缺 dream 依赖时是「永不触发」，不是「启动崩」");
    }

    /// <summary>
    /// 容器级复现：首回合插件尚未加载（激活抛错），后续回合就绪 —— 必须能在**同一个容器**里
    /// 从「降级」恢复到「触发」。这条覆盖的是构造期抓依赖那种写法做不到的事。
    /// </summary>
    [Fact]
    public async Task Container_RecoversAfterDreamSubsystemBecomesAvailable() {
        var ready = false;
        var fired = new TaskCompletionSource();
        var dream = new Mock<JoinCode.Dream.IDreamFeature>();
        dream.Setup(d => d.ExecuteAsync(It.IsAny<JoinCode.Dream.DreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns((JoinCode.Dream.DreamRequest request, CancellationToken token) => {
                fired.TrySetResult();
                return Task.FromResult(JoinCode.Dream.DreamResult.Success("ok", "task-1", 1, 5));
            });
        var scanner = new Mock<JoinCode.Dream.Services.ISessionScanner>();
        scanner.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["s1"]);

        var config = new JoinCode.Dream.AutoDreamConfig { MinHours = 0, MinSessions = 1, ProjectDir = "/proj-recover" };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);
        services.AddSingleton(new JoinCode.Dream.Services.ConsolidationLock(
            config, new Testing.Common.Services.InMemoryFileSystem()));
        services.AddSingleton(scanner.Object);
        services.AddSingleton<JoinCode.Dream.IDreamFeature>(_ => ready
            ? dream.Object
            : throw new InvalidOperationException("DreamPlugin 尚未加载完成"));
        services.RegisterBaseMiddlewareServices();

        await using var sp = services.BuildServiceProvider();
        var hook = sp.GetServices<Core.Query.StopHooks.IQueryStopHook>().Single();

        (await hook.OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true)).ShouldStop.Should().BeFalse();
        fired.Task.IsCompleted.Should().BeFalse("插件未就绪时不应发起整合");

        ready = true;
        await hook.OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true);
        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        dream.Verify(d => d.ExecuteAsync(It.IsAny<JoinCode.Dream.DreamRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 真机容器形态：Dream 服务在**插件自己的容器**里（<c>WorkflowPluginHost</c>），主容器拿不到。
    /// 这正是 TASK136 C1 真机探针暴露的差异 —— 既有用例都把 Dream 服务塞进主容器，
    /// 「每段单测绿、拼起来却不触发」的缝就一直没被覆盖。钩子必须能从插件宿主取到服务。
    /// </summary>
    [Fact]
    public async Task PluginHostContainer_ResolvesDreamAndFires() {
        var fired = new TaskCompletionSource();
        var dream = new Mock<JoinCode.Dream.IDreamFeature>();
        dream.Setup(d => d.ExecuteAsync(It.IsAny<JoinCode.Dream.DreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns((JoinCode.Dream.DreamRequest request, CancellationToken token) => {
                fired.TrySetResult();
                return Task.FromResult(JoinCode.Dream.DreamResult.Success("ok", "task-1", 1, 5));
            });
        var scanner = new Mock<JoinCode.Dream.Services.ISessionScanner>();
        scanner.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["s1"]);
        var config = new JoinCode.Dream.AutoDreamConfig { MinHours = 0, MinSessions = 1, ProjectDir = "/proj-plugin-host" };

        await using var host = new WorkflowPluginHost(new ConfiguringPlugin("Dream", services => {
            services.AddSingleton(config);
            services.AddSingleton(new JoinCode.Dream.Services.ConsolidationLock(
                config, new Testing.Common.Services.InMemoryFileSystem()));
            services.AddSingleton(scanner.Object);
            services.AddSingleton(dream.Object);
        }));
        (await host.LoadAsync().ConfigureAwait(true)).Success.Should().BeTrue();

        var manager = new Mock<IPluginManager>();
        manager.Setup(m => m.GetWorkflowPlugin("Dream")).Returns(host);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(manager.Object);
        services.RegisterBaseMiddlewareServices();

        await using var sp = services.BuildServiceProvider();
        var hook = sp.GetServices<Core.Query.StopHooks.IQueryStopHook>().Single();

        var result = await hook.OnStopAsync(Context(), CancellationToken.None).ConfigureAwait(true);
        result.ShouldStop.Should().BeFalse();
        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
    }

    /// <summary>测试用工作流插件 — 把服务注册进插件宿主自己的容器（模拟 DreamPlugin.LoadAsync）。</summary>
    private sealed class ConfiguringPlugin(string name, Action<IServiceCollection> configure) : IWorkflowPlugin {
        public string Name => name;
        /// <summary>插件版本。</summary>
        public string Version => "1.0.0";
        /// <summary>插件描述。</summary>
        public string Description => "test";
        /// <summary>注册服务。</summary>
        public Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default) {
            ctx.ConfigureServices(configure);
            return Task.FromResult(OperationResult.Ok());
        }
        /// <summary>初始化。</summary>
        public Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());
        /// <summary>卸载。</summary>
        public Task<PluginUnloadResult> UnloadAsync()
            => Task.FromResult(PluginUnloadResult.Success(name, TimeSpan.Zero));
    }

    /// <summary>
    /// 装配级串通：注册 → DI → <c>QueryStopHookManager</c>（构造期收集）→ 钩子 → 触发器 → 执行体。
    /// 这条用例补的是「每一段单测都绿、拼起来却不通」的缝（TASK100 §25 的老问题）。
    /// </summary>
    [Fact]
    public async Task ContainerWiring_QueryStopHookManager_ReachesDreamFeature() {
        var fired = new TaskCompletionSource();
        var dream = new Mock<JoinCode.Dream.IDreamFeature>();
        dream.Setup(d => d.ExecuteAsync(It.IsAny<JoinCode.Dream.DreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns((JoinCode.Dream.DreamRequest request, CancellationToken token) => {
                fired.TrySetResult();
                return Task.FromResult(JoinCode.Dream.DreamResult.Success("ok", "task-1", 1, 5));
            });
        var scanner = new Mock<JoinCode.Dream.Services.ISessionScanner>();
        scanner.Setup(s => s.ListSessionsTouchedSinceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["s1"]);

        var config = new JoinCode.Dream.AutoDreamConfig { MinHours = 0, MinSessions = 1, ProjectDir = "/proj-wiring" };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);
        services.AddSingleton(new JoinCode.Dream.Services.ConsolidationLock(
            config, new Testing.Common.Services.InMemoryFileSystem()));
        services.AddSingleton(dream.Object);
        services.AddSingleton(scanner.Object);
        services.RegisterBaseMiddlewareServices();
        services.AddSingleton<Core.Query.StopHooks.IQueryStopHookManager, Core.Query.StopHooks.QueryStopHookManager>();

        await using var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<Core.Query.StopHooks.IQueryStopHookManager>();

        var result = await manager.ExecuteStopHooksAsync("session-1", "query-complete").ConfigureAwait(true);
        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        result.ShouldStop.Should().BeTrue("无人阻止停止时管理器末尾返回 Stop（钩子返回 Continue 不改变结论）");
        dream.Verify(d => d.ExecuteAsync(It.IsAny<JoinCode.Dream.DreamRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
