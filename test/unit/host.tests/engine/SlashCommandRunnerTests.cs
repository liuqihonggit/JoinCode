namespace Host.Tests.Engine;

/// <summary>
/// SlashCommandRunner DI 解析测试（TASK065 F4）— DI 优先 + 静态兜底双路径行为锁定：
/// DI 注册 registry 时复用注入实例执行（探测命令不在生成器全量清单 → 兜底路径必然 Unknown，
/// 以此区分两条路径）；DI 缺席 registry 时静态兜底走 GeneratedCommandRegistration 全量清单。
/// </summary>
public sealed class SlashCommandRunnerTests {
    /// <summary>探测命令 — 仅存在于测试注入的 registry（生成器全量清单不含 → 区分 DI 路径与兜底路径）。</summary>
    private sealed class ProbeCommand : IChatCommand {
        public int Executions { get; private set; }
        public string Name => "probe-di";
        public string Description => "DI 路径探测";
        public string Usage => "/probe-di";
        public string[] Aliases => [];
        public string ArgumentHint => string.Empty;
        public bool IsHidden => false;

        public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
            Executions++;
            System.Console.WriteLine("probe executed");
            return Task.FromResult(ChatCommandResult.Continue());
        }
    }

    /// <summary>最小 DI 容器 — 命令执行必需三服务（BuildCommandServiceProvider 契约）+ 工具注册表（CmdMap 路由面）。</summary>
    private static IServiceCollection MinimalServices() {
        var services = new ServiceCollection();
        services.AddSingleton<IChatService>(Mock.Of<IChatService>());
        services.AddSingleton<ICodeService>(Mock.Of<ICodeService>());
        services.AddSingleton<IPlanService>(Mock.Of<IPlanService>());
        services.AddSingleton<IToolRegistry>(Mock.Of<IToolRegistry>());
        return services;
    }

    /// <summary>DI 注册 registry → 复用注入实例执行（不重建全量清单 — F4 消除的纯开销路径）。</summary>
    [Fact]
    public async Task RunAsync_UsesDiRegistry_WhenRegistered() {
        var probe = new ProbeCommand();
        var registry = new ChatCommandRegistry();
        registry.Register(probe);
        using var provider = MinimalServices().AddSingleton(registry).BuildServiceProvider();

        var result = await SlashCommandRunner.RunAsync("/probe-di", provider);

        Assert.True(result.Handled);
        Assert.Equal(1, probe.Executions);
    }

    /// <summary>DI 缺席 registry → 静态兜底全量清单（探测命令 Unknown — 兜底路径不含注入命令）。</summary>
    [Fact]
    public async Task RunAsync_FallsBackToGenerated_WhenNotRegistered() {
        using var provider = MinimalServices().BuildServiceProvider();

        var result = await SlashCommandRunner.RunAsync("/probe-di", provider);

        Assert.False(result.Handled);
    }
}
