namespace Composition.Tests.Commands;

/// <summary>
/// /export 命令单元测试 — 覆盖 --clipboard(服务可用复制内容/服务缺失降级)/指定文件名导出(.txt 后缀强制/
/// 内容渲染用户与助手角色)/无参非交互自动保存(智能文件名 sanitize)/空历史占位文案
/// <para>ChatService/ClipboardService 经本地构造 CommandServices 注入;文件写入经 InMemoryFileSystem 验证(0磁盘IO)</para>
/// </summary>
[Collection("command-output")]
public sealed class ExportCommandTest {
    private static (ExportCommand Command, Mock<IClipboardService>? Clipboard, IFileSystem Fs, ChatCommandContext Context) Arrange(
        string arguments = "",
        Mock<IChatService>? chatService = null,
        Mock<IClipboardService>? clipboard = null) {
        var chat = chatService ?? new Mock<IChatService>();
        var services = new ServiceCollection();
        var commandServices = new CommandServices {
            ChatService = chat.Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
            ClipboardService = clipboard?.Object,
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: arguments);
        return (new ExportCommand(), clipboard, commandServices.FileSystem, context);
    }

    private static Mock<IChatService> CreateHistoryChat() {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ApiMessageRecord> {
            new() { Role = "user", Content = "帮我写一个单元测试" },
            new() { Role = "assistant", Content = "好的，下面是示例代码" },
        });
        return chat;
    }

    [Fact]
    public async Task Execute_ClipboardFlagWithService_CopiesRenderedContent() {
        var clipboard = new Mock<IClipboardService>();
        var (cmd, _, _, context) = Arrange(arguments: "--clipboard", chatService: CreateHistoryChat(), clipboard: clipboard);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("已复制对话到剪贴板");
        clipboard.Verify(c => c.SetTextAsync(
            It.Is<string>(content =>
                content.Contains("# 对话导出") &&
                content.Contains("## 👤 用户") &&
                content.Contains("帮我写一个单元测试") &&
                content.Contains("## 🤖 助手") &&
                content.Contains("好的，下面是示例代码")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_ClipboardFlagWithoutService_ShowsUnavailable() {
        var (cmd, _, _, context) = Arrange(arguments: "--clipboard", clipboard: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("剪贴板服务不可用", "剪贴板服务缺失时应降级提示而非抛异常");
    }

    [Fact]
    public async Task Execute_WithFilename_WritesTxtFileWithConversation() {
        var (cmd, _, fs, context) = Arrange(arguments: "export-unit-test.md", chatService: CreateHistoryChat());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        var expectedPath = Path.GetFullPath("export-unit-test.txt");
        output.Should().Contain($"已导出到: {expectedPath}", "导出提示应包含绝对路径");
        fs.FileExists(expectedPath).Should().BeTrue("导出应真实写入文件系统");
        (await fs.ReadAllTextAsync(expectedPath)).Should().Contain("帮我写一个单元测试");
    }

    [Fact]
    public async Task Execute_WithDotMdFilename_ForcesTxtExtension() {
        var (cmd, _, fs, context) = Arrange(arguments: "export-ext-check.json", chatService: CreateHistoryChat());

        await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        var txtPath = Path.GetFullPath("export-ext-check.txt");
        var jsonPath = Path.GetFullPath("export-ext-check.json");
        fs.FileExists(txtPath).Should().BeTrue("非 .txt 后缀应被强制替换为 .txt");
        fs.FileExists(jsonPath).Should().BeFalse("原始后缀不应保留双份文件");
    }

    [Fact]
    public async Task Execute_NoArgsNonInteractive_AutoSavesWithSmartFilename() {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ApiMessageRecord> {
            new() { Role = "user", Content = "Hello World Query" },
        });
        var (cmd, _, _, context) = Arrange(arguments: "", chatService: chat);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已导出到: ", "非交互环境无参时应自动保存到文件");
        output.Should().Contain("hello-world-query", "智能文件名应从首条用户消息 sanitize 派生(小写+连字符)");
    }

    [Fact]
    public async Task Execute_NoArgsNonInteractive_PureCjkFirstLine_FallsBackToConversationName() {
        // 对齐 TS sanitizeFilename: replace(/[^a-z0-9\s-]/g,'') — 纯 CJK 首行全剥离 → 回退 conversation-{timestamp}.txt
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ApiMessageRecord> {
            new() { Role = "user", Content = "帮我写一个单元测试" },
        });
        var (cmd, _, _, context) = Arrange(arguments: "", chatService: chat);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("conversation-", "纯 CJK 首行被 TS 语义全剥离时应回退时间戳文件名");
        output.Should().NotContain("帮我", "CJK 字符不得出现在导出路径中（TS 基准仅保留 [a-z0-9\\s-]）");
    }

    [Fact]
    public async Task Execute_NoArgsNonInteractive_MixedCjkAscii_StripsCjkKeepsAscii() {
        // 对齐 TS: 混合首行仅保留 ASCII 段 — "帮我 Fix Bug 123" → "fix-bug-123"
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ApiMessageRecord> {
            new() { Role = "user", Content = "帮我 Fix Bug 123" },
        });
        var (cmd, _, _, context) = Arrange(arguments: "", chatService: chat);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("fix-bug-123", "CJK 剥离后 ASCII 段应保留并按空白转连字符");
        output.Should().NotContain("帮我", "混合首行中的 CJK 部分应被剥离");
    }

    [Fact]
    public async Task Execute_NoArgsNonInteractive_MultipleHyphenRuns_CollapseToSingle() {
        // 对齐 TS: replace(/-+/g,'-') — 5 连字符与连续空白均收敛为单个 '-'
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ApiMessageRecord> {
            new() { Role = "user", Content = "Fix-----Bug" },
        });
        var (cmd, _, _, context) = Arrange(arguments: "", chatService: chat);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("fix-bug", "5 连字符应完全收敛为单个（TS -+ 语义，非两次逐级 Replace）");
        output.Should().NotContain("fix--", "不得残留连续连字符");
    }

    [Fact]
    public async Task Execute_NoArgsNonInteractive_LongFirstLine_NoEllipsisInFilename() {
        // 对齐 TS: 截断发生在 sanitize 前（extractFirstPrompt 49+'…'，'…' 随后被剥离）— 文件名不含省略号
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ApiMessageRecord> {
            new() { Role = "user", Content = new string('a', 60) },
        });
        var (cmd, _, _, context) = Arrange(arguments: "", chatService: chat);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain(new string('a', 49), "截断应保留前 49 个原始字符");
        output.Should().NotContain("…", "省略号应被 sanitize 剥离，不得出现在导出路径中");
    }

    [Fact]
    public async Task Execute_EmptyHistory_WritesPlaceholderContent() {
        var (cmd, _, fs, context) = Arrange(arguments: "export-empty.txt", chatService: new Mock<IChatService>());

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("已导出到: ");
        var content = await fs.ReadAllTextAsync(Path.GetFullPath("export-empty.txt"));
        content.Should().Contain("(暂无对话记录)", "空历史导出应包含占位文案");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ExportCommand();
        cmd.Name.Should().Be("export");
        cmd.Usage.Should().Be("/export [filename|--clipboard]");
        cmd.Aliases.Should().BeEmpty();
    }
}
