namespace Composition.Tests.Commands;

/// <summary>
/// /history 命令单元测试 — 覆盖空历史(null/空列表)提示/多角色消息渲染/元数据一致性
/// <para>ChatService 为 CommandServices 必填 init 字段,用 Moq 桩自建服务包注入(对齐 MemoryCommandTest 形态)</para>
/// </summary>
[Collection("command-output")]
public sealed class HistoryCommandTest {
    private static (HistoryCommand Command, ChatCommandContext Context) Arrange(
        Mock<IChatService> chatService,
        string arguments = "") {
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
        return (new HistoryCommand(), context);
    }

    [Fact]
    public async Task Execute_EmptyHistory_ShowsPlaceholder() {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessageRecord>());
        var (cmd, context) = Arrange(chat);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 聊天历史 ===");
        output.Should().Contain("(暂无聊天历史)", "空列表应显示占位提示而非空输出");
    }

    [Fact]
    public async Task Execute_NullHistory_ShowsPlaceholder() {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
#pragma warning disable JCC11003 // 边界用例：生产代码显式处理 null 历史(is null 分支)，mock 需注入 null 验证该防御路径
            .Returns(Task.FromResult<IReadOnlyList<ApiMessageRecord>>(null!));
#pragma warning restore JCC11003
        var (cmd, context) = Arrange(chat);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("null 历史应降级为占位提示而非崩溃");
        output.Should().Contain("=== 聊天历史 ===");
        output.Should().Contain("(暂无聊天历史)");
    }

    [Fact]
    public async Task Execute_WithMessages_RendersRoleAndContent() {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessageRecord> {
                new() { Role = "user", Content = "你好，帮我写个冒泡排序" },
                new() { Role = "assistant", Content = "好的，这是冒泡排序实现" },
            });
        var (cmd, context) = Arrange(chat);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        chat.Verify(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()), Times.Once,
            "应查询一次消息列表");
        output.Should().Contain("[user]: 你好，帮我写个冒泡排序");
        output.Should().Contain("[assistant]: 好的，这是冒泡排序实现", "消息应按角色: 内容格式渲染");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new HistoryCommand();
        cmd.Name.Should().Be("history");
        cmd.Usage.Should().Be("/history");
        cmd.Description.Should().Contain("聊天历史");
        cmd.Aliases.Should().Contain("hist", "hist 为 /history 的别名");
        cmd.ExposeToMcp.Should().BeTrue("history 暴露给 MCP 供 AI 调用");
    }
}
