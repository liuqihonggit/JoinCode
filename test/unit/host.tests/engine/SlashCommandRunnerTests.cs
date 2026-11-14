namespace Host.Tests.Engine;

/// <summary>
/// SlashCommandRunner NodeRegistry 路径测试 — DSG051 阶段4 移除旧路径后，
/// INodeRegistry + IProfileManager 成为唯一命令来源，不再有 ChatCommandRegistry 回退。
/// </summary>
public sealed class SlashCommandRunnerTests {
    /// <summary>探测命令 — 仅存在于 NodeRegistry 注入的 CommandCategoryNode 中。</summary>
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

    /// <summary>最小 DI 容器 — 命令执行必需服务。</summary>
    private static IServiceCollection MinimalServices() {
        var services = new ServiceCollection();
        services.AddSingleton<IChatService>(Mock.Of<IChatService>());
        services.AddSingleton<ICodeService>(Mock.Of<ICodeService>());
        services.AddSingleton<IPlanService>(Mock.Of<IPlanService>());
        services.AddSingleton<IToolRegistry>(Mock.Of<IToolRegistry>());
        services.AddSingleton<IFileSystem>(IO.FileSystem.FileSystemFactory.Create());
        return services;
    }

    /// <summary>构造 mock INodeRegistry — 返回包含探测命令的 CommandCategoryNode。</summary>
    private static INodeRegistry BuildNodeRegistryWithProbe(ProbeCommand probe) {
        var category = ChatCommandCategory.Other;
        var node = new CommandCategoryNode($"{category.ToValue()}-command", category, new[] { probe });
        var mock = new Mock<INodeRegistry>();
        mock.Setup(r => r.GetServicesForConsumerAsync<CommandCategoryNode>(
            "command-dispatch", It.IsAny<ResolvedProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node });
        return mock.Object;
    }

    /// <summary>构造 mock IProfileManager — 返回 base Profile。</summary>
    private static IProfileManager BuildProfileManager() {
        var profiles = JoinCode.Composition.NodeRegistry.NodeProfileLoader.LoadFromResources();
        var resolver = new JoinCode.Composition.NodeRegistry.NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("base");
        var mock = new Mock<IProfileManager>();
        mock.SetupGet(m => m.Current).Returns(resolved);
        mock.SetupGet(m => m.AvailableProfiles).Returns(profiles.Keys.Order().ToArray());
        return mock.Object;
    }

    /// <summary>NodeRegistry 包含探测命令 → 通过 NodeRegistry 路径执行。</summary>
    [Fact]
    public async Task RunAsync_ExecutesCommand_FromNodeRegistry() {
        var probe = new ProbeCommand();
        using var provider = MinimalServices()
            .AddSingleton(BuildNodeRegistryWithProbe(probe))
            .AddSingleton(BuildProfileManager())
            .BuildServiceProvider();

        var result = await SlashCommandRunner.RunAsync("/probe-di", provider);

        Assert.True(result.Handled);
        Assert.Equal(1, probe.Executions);
    }

    /// <summary>NodeRegistry 不包含探测命令 → 命令未被识别（Unknown）。</summary>
    [Fact]
    public async Task RunAsync_UnknownCommand_WhenNotInNodeRegistry() {
        var mockRegistry = new Mock<INodeRegistry>();
        mockRegistry.Setup(r => r.GetServicesForConsumerAsync<CommandCategoryNode>(
            "command-dispatch", It.IsAny<ResolvedProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CommandCategoryNode>());
        using var provider = MinimalServices()
            .AddSingleton(mockRegistry.Object)
            .AddSingleton(BuildProfileManager())
            .BuildServiceProvider();

        var result = await SlashCommandRunner.RunAsync("/probe-di", provider);

        Assert.False(result.Handled);
    }
}
