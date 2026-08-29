namespace JoinCode.Entry.Tests;


/// <summary>
/// InitDebugDumpStep 单元测试 — 验证根据 DebugDumpChoice 位标志决定 dump 行为
/// 覆盖: None 跳过、All dump 全部、Prompt 仅 dump 提示词、JSON 模式跳过
/// </summary>
public class InitDebugDumpStepTests
{
    private readonly Mock<IDebugLogBuffer> _debugLogBuffer;
    private readonly Mock<ICrashSnapshotStore> _crashSnapshotStore;
    private readonly Mock<ISystemPromptProvider> _systemPromptProvider;
    private readonly Mock<IToolRegistry> _toolRegistry;
    private readonly IServiceProvider _serviceProvider;

    public InitDebugDumpStepTests()
    {
        _debugLogBuffer = new Mock<IDebugLogBuffer>();
        _crashSnapshotStore = new Mock<ICrashSnapshotStore>();
        _systemPromptProvider = new Mock<ISystemPromptProvider>();
        _toolRegistry = new Mock<IToolRegistry>();

        _debugLogBuffer.Setup(b => b.Count).Returns(0);
        _debugLogBuffer.Setup(b => b.GetRecent(It.IsAny<int>())).Returns([]);
        _debugLogBuffer.Setup(b => b.GetByLevel(It.IsAny<DebugLogLevel>(), It.IsAny<int>())).Returns([]);
        _crashSnapshotStore.Setup(s => s.TotalCount).Returns(0);
        _crashSnapshotStore.Setup(s => s.UnacknowledgedCount).Returns(0);
        _crashSnapshotStore.Setup(s => s.GetRecent(It.IsAny<int>())).Returns([]);
        _systemPromptProvider.Setup(p => p.GetSections()).Returns([]);
        _toolRegistry.Setup(r => r.GetCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var services = new ServiceCollection();
        services.AddSingleton(_debugLogBuffer.Object);
        services.AddSingleton(_crashSnapshotStore.Object);
        services.AddSingleton(_systemPromptProvider.Object);
        services.AddSingleton(_toolRegistry.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    private StartupContext CreateContext(DebugDumpSection choice, bool isJsonMode = false)
    {
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                ApiKey = "sk-test",
                Vendor = "openai",
                ModelId = "gpt-4o"
            }
        };

        var hostMock = new Mock<IHost>();
        hostMock.SetupGet(h => h.Services).Returns(_serviceProvider);

        return new StartupContext
        {
            Config = config,
            Options = new CommandLineOptions { JsonOutput = isJsonMode },
            Host = hostMock.Object,
            FileSystem = new InMemoryFileSystem(),
            DebugDumpChoice = choice,
        };
    }

    [Fact]
    public async Task NoneChoice_ShouldSkipDumpAndCallNext()
    {
        var step = new InitDebugDumpStep();
        var context = CreateContext(DebugDumpSection.None);
        var nextCalled = false;

        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("None 应直接调用 next");
        _systemPromptProvider.Verify(p => p.GetSections(), Times.Never, "None 不应触发任何渲染");
    }

    [Fact]
    public async Task JsonMode_ShouldSkipDumpEvenWithAllChoice()
    {
        var step = new InitDebugDumpStep();
        var context = CreateContext(DebugDumpSection.All, isJsonMode: true);
        var nextCalled = false;

        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("JSON 模式应直接调用 next");
        _systemPromptProvider.Verify(p => p.GetSections(), Times.Never, "JSON 模式不应 dump");
    }

    [Fact]
    public async Task AllChoice_ShouldDumpAllSections()
    {
        var step = new InitDebugDumpStep();
        var context = CreateContext(DebugDumpSection.All);
        var nextCalled = false;

        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("All 应在 dump 后调用 next");
        _systemPromptProvider.Verify(p => p.GetSections(), Times.AtLeastOnce, "All 应渲染系统提示词");
        _crashSnapshotStore.Verify(s => s.TotalCount, Times.AtLeastOnce, "All 应渲染初始化信息");
        _debugLogBuffer.Verify(b => b.GetRecent(It.IsAny<int>()), Times.AtLeastOnce, "All 应渲染诊断日志");
    }

    [Fact]
    public async Task PromptOnly_ShouldDumpOnlySystemPrompt()
    {
        var step = new InitDebugDumpStep();
        var context = CreateContext(DebugDumpSection.Prompt);
        var nextCalled = false;

        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("Prompt 应在 dump 后调用 next");
        _systemPromptProvider.Verify(p => p.GetSections(), Times.AtLeastOnce, "Prompt 应渲染系统提示词");
        _crashSnapshotStore.Verify(s => s.TotalCount, Times.Never, "Prompt 不应渲染初始化信息");
        _debugLogBuffer.Verify(b => b.GetRecent(It.IsAny<int>()), Times.Never, "Prompt 不应渲染诊断日志");
    }

    [Fact]
    public async Task InitOnly_ShouldDumpOnlyInitInfo()
    {
        var step = new InitDebugDumpStep();
        var context = CreateContext(DebugDumpSection.Init);
        var nextCalled = false;

        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("Init 应在 dump 后调用 next");
        _crashSnapshotStore.Verify(s => s.TotalCount, Times.AtLeastOnce, "Init 应渲染初始化信息");
        _systemPromptProvider.Verify(p => p.GetSections(), Times.AtLeastOnce, "Init 内部也会枚举 sections 列出名称");
        _debugLogBuffer.Verify(b => b.GetRecent(It.IsAny<int>()), Times.Never, "Init 不应渲染诊断日志");
    }

    [Fact]
    public async Task LogOnly_ShouldDumpOnlyLogs()
    {
        var step = new InitDebugDumpStep();
        var context = CreateContext(DebugDumpSection.Log);
        var nextCalled = false;

        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("Log 应在 dump 后调用 next");
        _debugLogBuffer.Verify(b => b.GetRecent(It.IsAny<int>()), Times.AtLeastOnce, "Log 应渲染诊断日志");
        _crashSnapshotStore.Verify(s => s.TotalCount, Times.Never, "Log 不应渲染初始化信息");
    }

    [Fact]
    public async Task CombinedInitAndPrompt_ShouldDumpBoth()
    {
        var step = new InitDebugDumpStep();
        var context = CreateContext(DebugDumpSection.Init | DebugDumpSection.Prompt);
        var nextCalled = false;

        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("组合标志应在 dump 后调用 next");
        _crashSnapshotStore.Verify(s => s.TotalCount, Times.AtLeastOnce, "组合含 Init 应渲染初始化信息");
        _systemPromptProvider.Verify(p => p.GetSections(), Times.AtLeastOnce, "组合含 Prompt 应渲染系统提示词");
        _debugLogBuffer.Verify(b => b.GetRecent(It.IsAny<int>()), Times.Never, "组合不含 Log 不应渲染诊断日志");
    }
}
