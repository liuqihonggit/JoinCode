namespace Composition.Tests.Commands;

/// <summary>
/// /context 命令单元测试 — 覆盖主路径统计渲染(模型/Token/角色分类)/FastMode 激活显示快速模型/
/// 未知模型回退 128k 默认窗口/空历史零 Token/FastModeService 缺失回退 unknown 模型名/
/// ModelCatalog 缺失抛 [APP001](服务缺失降级缺失,bug 登记快照)
/// </summary>
[Collection("command-output")]
public sealed class ContextCommandTest {
    private static Mock<IChatService> CreateChatWithHistory(IReadOnlyList<ApiMessageRecord> history) {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        return chat;
    }

    private static Mock<IModelCatalog> CreateCatalog(int? contextWindow = 200_000) {
        var catalog = new Mock<IModelCatalog>();
        catalog.Setup(c => c.FindModelContextWindow(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(contextWindow);
        return catalog;
    }

    /// <summary>构造 /context 测试桩 — withCatalog=false 模拟精简启动形态(目录未注册),fastMode/workflowConfig 按需</summary>
    private static (ContextCommand Command, ChatCommandContext Context) Arrange(
        IReadOnlyList<ApiMessageRecord> history,
        Mock<IModelCatalog>? catalog = null,
        Mock<IFastModeService>? fastMode = null,
        WorkflowConfig? workflowConfig = null,
        bool withCatalog = true) {
        var chat = CreateChatWithHistory(history);
        var services = new ServiceCollection();
        if (withCatalog) services.AddSingleton((catalog ?? CreateCatalog()).Object);
        if (fastMode is not null) services.AddSingleton(fastMode.Object);
        var commandServices = new CommandServices {
            ChatService = chat.Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
            WorkflowConfig = workflowConfig,
        };
        var context = new ChatCommandContext {
            Arguments = "",
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };
        return (new ContextCommand(), context);
    }

    private static IReadOnlyList<ApiMessageRecord> CreateHistory() => [
        new() { Role = "user", Content = "abcd" },
        new() { Role = "user", Content = "abcd" },
        new() { Role = "assistant", Content = "abcdefgh" },
    ];

    [Fact]
    public async Task Execute_WithHistory_RendersContextStats() {
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns("test-model");
        fastMode.Setup(f => f.IsFastModeActive).Returns(false);
        var (cmd, context) = Arrange(CreateHistory(), fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Context Window");
        output.Should().Contain("Model: test-model");
        output.Should().Contain("Tokens: 4 / 200,000", "16 字符 / 4 = 4 Token,窗口来自 catalog");
        output.Should().Contain("USER", "角色分类条目应大写渲染");
        output.Should().Contain("ASSISTANT");
        output.Should().Contain("会话开始:");
        output.Should().Contain("会话时长:");
        output.Should().Contain("消息数量: 3");
    }

    [Fact]
    public async Task Execute_FastModeActive_ShowsFastModelId() {
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns("primary-model");
        fastMode.Setup(f => f.FastModelId).Returns("fast-model");
        fastMode.Setup(f => f.IsFastModeActive).Returns(true);
        var (cmd, context) = Arrange(CreateHistory(), fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Model: fast-model", "FastMode 激活时上下文统计应显示快速模型");
    }

    [Fact]
    public async Task Execute_UnknownModelWindow_DefaultsTo128k() {
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns("mystery-model");
        fastMode.Setup(f => f.IsFastModeActive).Returns(false);
        var (cmd, context) = Arrange(CreateHistory(), catalog: CreateCatalog(contextWindow: null), fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Tokens: 4 / 128,000", "catalog 未收录模型时应回退 128k 默认窗口");
    }

    [Fact]
    public async Task Execute_EmptyHistory_ShowsZeroTokens() {
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns("test-model");
        fastMode.Setup(f => f.IsFastModeActive).Returns(false);
        var (cmd, context) = Arrange([], fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Tokens: 0 / 200,000");
        output.Should().Contain("消息数量: 0");
    }

    [Fact]
    public async Task Execute_WithoutFastModeService_FallsBackToUnknownModel() {
        var (cmd, context) = Arrange(CreateHistory(), fastMode: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("FastModeService 缺失应降级为 unknown 模型名而非崩溃");
        output.Should().Contain("Model: unknown");
        output.Should().Contain("Tokens: 4 / 200,000");
    }

    /// <summary>生产 bug 已修复(主代理): ModelCatalog 缺失(精简启动形态)降级 128k 默认窗口渲染
    ///（对齐"未知模型回退"同一语义 — 不再抛 [APP001] 传播 REPL，对齐 EffortCommand 降级先例）。</summary>
    [Fact]
    public async Task Execute_WithoutModelCatalog_RendersWithDefaultWindow() {
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns("test-model");
        fastMode.Setup(f => f.IsFastModeActive).Returns(false);
        var (cmd, context) = Arrange(CreateHistory(), fastMode: fastMode, withCatalog: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("目录缺失属降级场景非命令失败");
        output.Should().Contain("128,000", "目录缺失降级默认窗口 128_000（千分位渲染）");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ContextCommand();
        cmd.Name.Should().Be("context");
        cmd.Usage.Should().Be("/context");
        cmd.Description.Should().Contain("上下文统计");
    }
}
