namespace Composition.Tests.Commands;

/// <summary>
/// /review 命令单元测试 — 覆盖空参非交互直跑(gh pr list 模板)/PR 号模板(gh pr view+diff)/
/// 流式输出主路径/LLM 流异常降级/流取消提示
/// <para>ChatService 用 Moq 桩经本地构造 CommandServices 注入(对齐 CompactCommandTest 先例),
/// SendMessageStreamAsync 捕获 prompt 断言模板不变量;测试环境非交互跳过 Dialog 选择器</para>
/// </summary>
[Collection("command-output")]
public sealed class ReviewCommandTest {
    private static async IAsyncEnumerable<string> StreamOf(params string[] chunks) {
        foreach (var chunk in chunks) yield return chunk;
    }

    private static (ReviewCommand Command, Mock<IChatService> Chat, ChatCommandContext Context) Arrange(
        string arguments = "",
        string[]? streamChunks = null,
        Exception? streamError = null) {
        var chat = new Mock<IChatService>();
        if (streamError is not null) {
            chat.Setup(c => c.SendMessageStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Throws(streamError);
        } else {
            chat.Setup(c => c.SendMessageStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken _) => StreamOf(streamChunks ?? ["审查意见 A"]));
        }

        var commandServices = new CommandServices {
            ChatService = chat.Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: arguments);
        return (new ReviewCommand(), chat, context);
    }

    [Fact]
    public async Task Execute_EmptyArgs_NonInteractive_UsesPrListPrompt() {
        var (cmd, chat, context) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("正在审查...");
        chat.Verify(c => c.SendMessageStreamAsync(
            It.Is<string>(p => p.Contains("gh pr list")), It.IsAny<CancellationToken>()), Times.Once,
            "无 PR 号模板应指示 LLM 先 gh pr list 列出 PR");
    }

    [Fact]
    public async Task Execute_WithPrNumber_UsesPrViewAndDiffPrompt() {
        var (cmd, chat, context) = Arrange(arguments: "123");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("正在审查...");
        chat.Verify(c => c.SendMessageStreamAsync(
            It.Is<string>(p => p.Contains("gh pr view 123") && p.Contains("gh pr diff 123")),
            It.IsAny<CancellationToken>()), Times.Once, "PR 号模板应指示 LLM 查看 PR 详情与 diff");
    }

    [Fact]
    public async Task Execute_NumericArg_StillTreatedAsPrNumber() {
        var (cmd, chat, context) = Arrange(arguments: "  42  ");

        var (_, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        chat.Verify(c => c.SendMessageStreamAsync(
            It.Is<string>(p => p.Contains("gh pr view 42")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_StreamEmitsChunks_PassesPromptToChatService() {
        var (cmd, chat, context) = Arrange(arguments: "7", streamChunks: ["第一段", "第二段"]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        chat.Verify(c => c.SendMessageStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        output.Should().Contain("正在审查...", "流式输出由 ChatService 内部处理,命令侧只输出审查中提示");
    }

    [Fact]
    public async Task Execute_StreamThrows_ShowsErrorWithExceptionMessage() {
        var (cmd, chat, context) = Arrange(arguments: "9", streamError: new InvalidOperationException("llm down"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("审查异常不应中断会话");
        output.Should().Contain("审查失败");
        output.Should().Contain("llm down");
    }

    [Fact]
    public async Task Execute_StreamCancelled_ShowsCancelledMessage() {
        var (cmd, chat, context) = Arrange(arguments: "9", streamError: new OperationCanceledException());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("审查已取消。");
        output.Should().NotContain("审查失败", "取消应走独立分支而非通用错误分支");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ReviewCommand();
        cmd.Name.Should().Be("review");
        cmd.Usage.Should().Be("/review [pr-number]");
        cmd.ExposeToMcp.Should().BeTrue();
    }
}
