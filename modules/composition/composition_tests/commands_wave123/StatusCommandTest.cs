namespace Composition.Tests.Commands;

/// <summary>
/// /status 命令单元测试 — 覆盖概览渲染(版本/工作目录/会话/Provider/模型)/消息历史统计(user/assistant 计数)/
/// Token 用量三分支(追踪器缺失/无数据/有数据)/快速模式模型替换/MCP 工具注册标记/模型目录缺失抛 [APP006]
/// <para>IModelCatalog/IFastModeService 经 DI 注入;ChatService/UsageTracker/ToolRegistry 经 CommandServices 注入
/// (harness 不支持的字段,故本地构造服务包)</para>
/// </summary>
[Collection("command-output")]
public sealed class StatusCommandTest {
    private static (StatusCommand Command, ChatCommandContext Context) Arrange(
        string arguments = "",
        Mock<IChatService>? chatService = null,
        Mock<IUsageTracker>? usageTracker = null,
        Mock<IModelCatalog>? modelCatalog = null,
        Mock<IFastModeService>? fastMode = null,
        Mock<IExecutionSettingsProvider>? executionSettings = null,
        WorkflowConfig? workflowConfig = null,
        IToolRegistry? toolRegistry = null) {
        var catalog = modelCatalog ?? new Mock<IModelCatalog>();
        catalog.Setup(c => c.GetProviderDisplayName(It.IsAny<string>())).Returns("OpenAI");
        var fast = fastMode ?? new Mock<IFastModeService>();
        var services = new ServiceCollection();
        services.AddSingleton(catalog.Object);
        services.AddSingleton(fast.Object);
        var commandServices = new CommandServices {
            ChatService = (chatService ?? new Mock<IChatService>()).Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
            UsageTracker = usageTracker?.Object,
            ExecutionSettingsProvider = executionSettings?.Object,
            WorkflowConfig = workflowConfig ?? new WorkflowConfig(),
            ToolRegistry = toolRegistry,
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: arguments);
        return (new StatusCommand(), context);
    }

    private static List<ApiMessageRecord> CreateHistory() => [
        new() { Role = "user", Content = "你好", Timestamp = DateTime.UtcNow.AddMinutes(-5) },
        new() { Role = "assistant", Content = "你好，有什么可以帮你？", Timestamp = DateTime.UtcNow.AddMinutes(-4) },
        new() { Role = "user", Content = "继续", Timestamp = DateTime.UtcNow.AddMinutes(-1) },
    ];

    [Fact]
    public async Task Execute_FullOverview_RendersCoreSections() {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreateHistory());
        var (cmd, context) = Arrange(chatService: chat,
            workflowConfig: new WorkflowConfig { Provider = new ProviderConfig { Vendor = "openai" } });

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("版本:       ");
        output.Should().Contain($"工作目录:   {TestFileSystem.Current.GetCurrentDirectory()}");
        output.Should().Contain("会话ID:     test-session");
        output.Should().Contain("Provider:   OpenAI");
        output.Should().Contain("API密钥:");
        output.Should().Contain("未配置", "无 ProviderDefinitionRegistry 时 API Key 应显示未配置");
        output.Should().Contain("MCP工具:");
        output.Should().Contain("未连接", "ToolRegistry 缺失时显示未连接");
        output.Should().Contain("总消息数:   3");
        output.Should().Contain("用户输入:   2");
        output.Should().Contain("AI回复:     1");
    }

    [Fact]
    public async Task Execute_WithoutUsageTracker_ShowsTrackerUnavailable() {
        var (cmd, context) = Arrange(usageTracker: null);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("用量追踪器不可用");
        output.Should().NotContain("暂无 Token 用量数据");
    }

    [Fact]
    public async Task Execute_TrackerWithZeroTokens_ShowsNoDataHint() {
        var tracker = new Mock<IUsageTracker>();
        tracker.Setup(t => t.GetSessionStatistics(It.IsAny<string>()))
            .Returns(new TokenUsageStatistics());
        var (cmd, context) = Arrange(usageTracker: tracker);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("暂无 Token 用量数据");
    }

    [Fact]
    public async Task Execute_TrackerWithUsage_ShowsTokenBreakdown() {
        var tracker = new Mock<IUsageTracker>();
        tracker.Setup(t => t.GetSessionStatistics("test-session")).Returns(new TokenUsageStatistics {
            TotalRequests = 2,
            TotalInputTokens = 100,
            TotalOutputTokens = 50,
        });
        var (cmd, context) = Arrange(usageTracker: tracker);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("输入: 100");
        output.Should().Contain("输出: 50");
        output.Should().Contain("总计: 150");
    }

    [Fact]
    public async Task Execute_ChatServiceFailure_DegradesToHistoryUnavailable() {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("history broken"));
        var (cmd, context) = Arrange(chatService: chat);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("无法获取对话历史", "历史获取失败应降级提示而非中断命令");
    }

    [Fact]
    public async Task Execute_FastModeActive_ShowsFastModelId() {
        var fast = new Mock<IFastModeService>();
        fast.Setup(f => f.IsFastModeActive).Returns(true);
        fast.Setup(f => f.FastModelId).Returns("gpt-fast-4");
        fast.Setup(f => f.PrimaryModelId).Returns("gpt-main-4");
        var (cmd, context) = Arrange(fastMode: fast);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("快速模式:   开启");
        output.Should().Contain("当前模型:   gpt-fast-4", "快速模式激活时应展示快速模型而非主模型");
    }

    [Fact]
    public async Task Execute_WithEffortLevel_ShowsEffortRow() {
        var settings = new Mock<IExecutionSettingsProvider>();
        settings.SetupGet(s => s.EffortLevel).Returns(EffortLevel.High);
        var (cmd, context) = Arrange(executionSettings: settings);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("推理力度:   high");
    }

    [Fact]
    public async Task Execute_WithToolRegistry_ShowsMcpRegistered() {
        var (cmd, context) = Arrange(toolRegistry: new Mock<IToolRegistry>().Object);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("MCP工具:");
        output.Should().Contain("已注册");
    }

    [Fact]
    public async Task Execute_WithoutModelCatalog_ThrowsApp006() {
        var services = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };
        var context = CommandTestHarness.CreateContext(services);

        var act = () => CommandTestHarness.CaptureOutputAsync(() => new StatusCommand().ExecuteAsync(context));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[APP006]*");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new StatusCommand();
        cmd.Name.Should().Be("status");
        cmd.Usage.Should().Be("/status");
        cmd.ExposeToMcp.Should().BeTrue("status 应暴露给 MCP");
    }
}
