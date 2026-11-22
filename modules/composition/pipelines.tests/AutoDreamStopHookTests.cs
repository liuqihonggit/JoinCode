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
        var hook = new AutoDreamStopHook(trigger, NullLogger<AutoDreamStopHook>.Instance);

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
