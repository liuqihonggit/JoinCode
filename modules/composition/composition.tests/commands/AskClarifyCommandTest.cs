namespace Composition.Tests.Commands;

/// <summary>
/// /?? (别名 /ask) 命令单元测试 — 覆盖带需求主路径(系统提示词前缀+响应含完成标记时退出循环)/
/// ChatService 异常收敛/退出命令识别纯函数/元数据一致性
/// <para>命令内部将 TerminalHelper 输出切到真实 stdout(绕过测试捕获),故断言以
/// ChatService 调用形态(Verify)与结果语义为主,输出文本不作强断言;
/// ChatService 用 Moq 桩自建 CommandServices 注入,不发真实请求</para>
/// </summary>
[Collection("command-output")]
public sealed class AskClarifyCommandTest {
    /// <summary>把事件序列包装为异步事件流 — 模拟 LLM 流式响应</summary>
    private static async IAsyncEnumerable<ChatStreamEvent> EventStream(
        IReadOnlyList<ChatStreamEvent> events,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) {
        foreach (var evt in events) {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return evt;
        }
    }

    private static (AskClarifyCommand Command, ChatCommandContext Context, Mock<IChatService> Chat) Arrange(
        string arguments = "做一个 todo 应用") {
        var chatService = new Mock<IChatService>();
        chatService.Setup(c => c.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string message, CancellationToken ct) => EventStream(
                new[] {
                    ChatStreamEvent.Text("【需求已明确】\n需求清单: 1. 增删改查 2. 本地存储"),
                }, ct));
        var commandServices = new CommandServices {
            ChatService = chatService.Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };
        var context = new ChatCommandContext {
            Arguments = arguments,
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };
        return (new AskClarifyCommand(), context, chatService);
    }

    [Fact]
    public async Task Execute_WithRequirement_SendsSystemPromptAndUserNeed() {
        var (cmd, context, chat) = Arrange("做一个 todo 应用");

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        chat.Verify(c => c.StreamWithEventsAsync(
            It.Is<string>(msg =>
                msg.StartsWith(AskClarifyPrompts.SystemPrompt, StringComparison.Ordinal)
                && msg.Contains("---")
                && msg.Contains("用户需求: 做一个 todo 应用")),
            It.IsAny<CancellationToken>()), Times.Once,
            "首轮消息应为系统提示词 + 分隔线 + 用户需求形态");
    }

    [Fact]
    public async Task Execute_ResponseContainsDoneMarker_ExitsLoopAfterSingleRound() {
        var (cmd, context, chat) = Arrange();

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        chat.Verify(c => c.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "响应含【需求已明确】标记应直接退出澄清循环,只发起一轮请求");
    }

    [Fact]
    public async Task Execute_DoneMarkerIsCaseInsensitive_AlsoExits() {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string message, CancellationToken ct) => EventStream(
                new[] { ChatStreamEvent.Text("【需求已明确】") }, ct));
        var commandServices = new CommandServices {
            ChatService = chat.Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };
        var context = new ChatCommandContext {
            Arguments = "需求",
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => new AskClarifyCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        chat.Verify(c => c.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "完成标记匹配应大小写不敏感并退出循环");
    }

    [Fact]
    public async Task Execute_ChatServiceThrows_ConvergesWithoutCrash() {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("provider unavailable"));
        var commandServices = new CommandServices {
            ChatService = chat.Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };
        var context = new ChatCommandContext {
            Arguments = "做一个 todo 应用",
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => new AskClarifyCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("ChatService 异常应被命令内部捕获收敛,不应向上抛出");
        chat.Verify(c => c.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("/end")]
    [InlineData("/done")]
    [InlineData("/exit")]
    [InlineData("/quit")]
    [InlineData("  /END  ")]
    public void IsExitCommand_RecognizesExitCommands(string input) {
        AskClarifyCommand.IsExitCommand(input).Should().BeTrue(
            $"'{input}' 应识别为退出命令(大小写不敏感+两端空白容忍)");
    }

    [Theory]
    [InlineData("/endd")]
    [InlineData("hello")]
    [InlineData("")]
    public void IsExitCommand_RejectsNonExitInput(string input) {
        AskClarifyCommand.IsExitCommand(input).Should().BeFalse($"'{input}' 不是退出命令");
    }

    [Fact]
    public void ClarifyDoneMarker_ContractValue() {
        AskClarifyCommand.ClarifyDoneMarker.Should().Be("【需求已明确】",
            "完成标记是 LLM 与宿主的协议契约,变更需两侧同步");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new AskClarifyCommand();
        cmd.Name.Should().Be("??");
        cmd.Aliases.Should().Contain("ask", "ask 为 /?? 的别名");
        cmd.Usage.Should().Contain("/??").And.Contain("/ask");
        cmd.Description.Should().Contain("需求澄清");
        cmd.ArgumentHint.Should().Be("[需求描述]");
    }
}
