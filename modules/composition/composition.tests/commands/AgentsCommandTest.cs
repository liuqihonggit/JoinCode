namespace Composition.Tests.Commands;

/// <summary>
/// /agents 命令单元测试（用户口径 agent-list 域）— 覆盖 list 默认分支(提供者缺失降级/空列表/多代理分组渲染)/
/// info(缺参用法/找到渲染详情/未找到)/未知操作引导
/// <para>IAgentDefinitionProvider 经 DI 容器解析,用 Moq 注入;列表在测试环境走非交互纯文本分支</para>
/// </summary>
[Collection("command-output")]
public sealed class AgentsCommandTest {
    private static (AgentsCommand Command, Mock<IAgentDefinitionProvider> Provider, ChatCommandContext Context) Arrange(
        string arguments = "",
        Mock<IAgentDefinitionProvider>? providerOverride = null) {
        var provider = providerOverride ?? new Mock<IAgentDefinitionProvider>();
        if (providerOverride is null) {
            provider.Setup(p => p.GetAgentDefinitionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        }
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(provider.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new AgentsCommand(), provider, context);
    }

    private static AgentDefinition CreateAgent(
        ExecutorVariant variant,
        string whenToUse,
        string? description = null,
        string? modelName = null,
        List<string>? tools = null) => new() {
        Role = AgentRole.Executor,
        Variant = variant,
        WhenToUse = whenToUse,
        Description = description,
        ModelName = modelName,
        Tools = tools ?? [],
    };

    [Fact]
    public async Task Execute_ListWithoutProvider_ShowsProviderUnavailable() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "list");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => new AgentsCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("代理定义提供者不可用");
    }

    [Fact]
    public async Task Execute_ListWithoutAgents_ShowsNoAgents() {
        var (cmd, _, context) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前无可用代理");
    }

    [Fact]
    public async Task Execute_ListWithAgents_RendersGroupedNonInteractiveList() {
        var provider = new Mock<IAgentDefinitionProvider>();
        provider.Setup(p => p.GetAgentDefinitionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateAgent(ExecutorVariant.Code, "执行编码任务", description: "代码执行专家"),
                CreateAgent(ExecutorVariant.Search, "执行搜索任务"),
            ]);
        var (cmd, _, context) = Arrange(arguments: "ls", providerOverride: provider);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 代理列表 ===");
        output.Should().Contain("[内置] (2)", "无 SourcePath 的代理应归入内置分组");
        output.Should().Contain("executor:code");
        output.Should().Contain("代码执行专家");
        output.Should().Contain("executor:search");
        output.Should().Contain("执行搜索任务");
        output.Should().Contain("使用 /agents info <name> 查看代理详情");
    }

    [Fact]
    public async Task Execute_InfoWithoutName_ShowsUsage() {
        var (cmd, _, context) = Arrange(arguments: "info");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法：/agents info <agent-type>");
    }

    [Fact]
    public async Task Execute_InfoWithAgent_RendersDetailFields() {
        var agent = CreateAgent(ExecutorVariant.Code, "执行编码任务",
            description: "代码执行专家", modelName: "deepseek-chat", tools: ["read_file", "write_file"]);
        var provider = new Mock<IAgentDefinitionProvider>();
        provider.Setup(p => p.GetAgentDefinitionAsync(
                AgentRole.Executor, ExecutorVariant.Code, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        var (cmd, _, context) = Arrange(arguments: "info code", providerOverride: provider);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 代理：executor:code ===");
        output.Should().Contain("描述：代码执行专家");
        output.Should().Contain("使用场景：执行编码任务");
        output.Should().Contain("后台模式：否");
        output.Should().Contain("模型：deepseek-chat");
        output.Should().Contain("允许工具 (2):");
        output.Should().Contain("+ read_file");
        output.Should().Contain("+ write_file");
    }

    [Fact]
    public async Task Execute_InfoWithUnknownAgent_ShowsNotFound() {
        var (cmd, _, context) = Arrange(arguments: "info ghost");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未找到代理：ghost");
    }

    [Fact]
    public async Task Execute_InfoWithoutProvider_ShowsProviderUnavailable() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "info code");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => new AgentsCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("代理定义提供者不可用");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsSupportedActions() {
        var (cmd, _, context) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知操作：bogus");
        output.Should().Contain("可用操作：list, info");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new AgentsCommand();
        cmd.Name.Should().Be("agents");
        cmd.Usage.Should().StartWith("/agents");
        cmd.Aliases.Should().BeEmpty();
    }
}
