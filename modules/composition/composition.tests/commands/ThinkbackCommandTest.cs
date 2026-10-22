namespace Composition.Tests.Commands;

/// <summary>
/// /thinkback 命令单元测试 — 覆盖 count 解析(空参默认1/数字/非数字/零负数钳位)/
/// 空记录提示/记录渲染(编号倒序/模型/时间/内容)/ModelId 省略/存储缺失降级/元数据
/// <para>ThinkingStore 用 Moq 桩经 CommandServices.ThinkingStore 可选字段注入</para>
/// </summary>
[Collection("command-output")]
public sealed class ThinkbackCommandTest {
    private static (ThinkbackCommand Command, ChatCommandContext Context, Mock<IThinkingStore> Store) Arrange(
        string arguments = "",
        Mock<IThinkingStore>? store = null) {
        var thinkingStore = store ?? new Mock<IThinkingStore>();
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            ThinkingStore = thinkingStore.Object,
        };
        var context = new ChatCommandContext {
            Arguments = arguments,
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };
        return (new ThinkbackCommand(), context, thinkingStore);
    }

    private static ThinkingEntry Entry(
        string content = "分析用户需求",
        string? modelId = "gpt-4o",
        DateTime? timestamp = null) => new() {
        SessionId = "test-session",
        Content = content,
        ModelId = modelId,
        Timestamp = timestamp ?? new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Execute_WithoutStore_ShowsUnavailable() {
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };
        var context = new ChatCommandContext {
            Arguments = "",
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ThinkbackCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("存储缺失应降级提示而非崩溃");
        output.Should().Contain("思考存储不可用");
    }

    [Fact]
    public async Task Execute_EmptyArgs_DefaultsToSingleEntry() {
        var store = new Mock<IThinkingStore>();
        store.Setup(s => s.GetRecentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ThinkingEntry>());
        var (cmd, context, _) = Arrange(store: store);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        store.Verify(s => s.GetRecentAsync("test-session", 1, It.IsAny<CancellationToken>()), Times.Once,
            "空参默认回放最近 1 条");
        output.Should().Contain("思考回放:");
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("10", 10)]
    public async Task Execute_NumericArgs_RequestsExactCount(string arguments, int expectedCount) {
        var store = new Mock<IThinkingStore>();
        store.Setup(s => s.GetRecentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ThinkingEntry>());
        var (cmd, context, _) = Arrange(arguments: arguments, store: store);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        store.Verify(s => s.GetRecentAsync("test-session", expectedCount, It.IsAny<CancellationToken>()), Times.Once,
            "数字参数应作为回放条数");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task Execute_InvalidArgs_FallBackToSingleEntry(string arguments) {
        var store = new Mock<IThinkingStore>();
        store.Setup(s => s.GetRecentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ThinkingEntry>());
        var (cmd, context, _) = Arrange(arguments: arguments, store: store);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        store.Verify(s => s.GetRecentAsync("test-session", 1, It.IsAny<CancellationToken>()), Times.Once,
            $"非法参数 '{arguments}' 应回退默认 1 条(非数字解析失败或零负数钳位到 1)");
    }

    [Fact]
    public async Task Execute_NoEntries_ShowsEmptyHint() {
        var store = new Mock<IThinkingStore>();
        store.Setup(s => s.GetRecentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ThinkingEntry>());
        var (cmd, context, _) = Arrange(store: store);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("  当前会话暂无思考过程记录");
        output.Should().Contain("  当 AI 使用 extended thinking 时将自动记录", "空记录应引导用户了解记录来源");
    }

    [Fact]
    public async Task Execute_WithEntry_RendersModelTimestampAndContent() {
        var store = new Mock<IThinkingStore>();
        store.Setup(s => s.GetRecentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ThinkingEntry> { Entry(content: "用户需要排序算法\n考虑性能优化") });
        var (cmd, context, _) = Arrange(store: store);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("  ── 思考 #1 ──");
        output.Should().Contain("  模型: gpt-4o");
        output.Should().Contain("  时间: 2026-01-15 10:30:00 UTC");
        output.Should().Contain("  用户需要排序算法");
        output.Should().Contain("  考虑性能优化", "多行内容应逐行渲染");
        output.Should().Contain("  共 1 条思考记录");
    }

    [Fact]
    public async Task Execute_EntryWithoutModel_OmitsModelLine() {
        var store = new Mock<IThinkingStore>();
        store.Setup(s => s.GetRecentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ThinkingEntry> { Entry(modelId: null) });
        var (cmd, context, _) = Arrange(store: store);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("── 思考 #1 ──");
        output.Should().NotContain("模型:", "ModelId 为空时不应渲染模型行");
    }

    [Fact]
    public async Task Execute_MultipleEntries_NumbersInReverseOrder() {
        var store = new Mock<IThinkingStore>();
        store.Setup(s => s.GetRecentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ThinkingEntry> {
                Entry(content: "较新的思考"),
                Entry(content: "较旧的思考"),
            });
        var (cmd, context, _) = Arrange(arguments: "2", store: store);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("── 思考 #2 ──");
        output.Should().Contain("── 思考 #1 ──", "列表首条(最新)应编号为总数");
        output.Should().Contain("共 2 条思考记录");
        store.Verify(s => s.GetRecentAsync("test-session", 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ThinkbackCommand();
        cmd.Name.Should().Be("thinkback");
        cmd.Usage.Should().Be("/thinkback [count]");
        cmd.Description.Should().Contain("思考过程");
        cmd.ArgumentHint.Should().Be("[count]");
    }
}
