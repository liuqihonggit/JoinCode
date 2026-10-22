namespace Composition.Tests.Commands;

/// <summary>
/// /compact 命令单元测试 — 覆盖空历史提示/主路径摘要压缩+渲染/自定义指令透传/
/// 摘要生成异常降级回退文案/CompactHistoryAsync PTL 异常回滚重试/回滚零移除走最终兜底/
/// internal 指标计算契约
/// <para>ChatService 用 Moq 桩(历史+摘要可控,IChatContextManager 经 DI 注入触发 PTL 重试分支)</para>
/// </summary>
[Collection("command-output")]
public sealed class CompactCommandTest {
    private const string RawSummary = "<summary>摘要要点内容</summary>";
    private const string FormattedSummary = "摘要：\n摘要要点内容";

    /// <summary>构造 /compact 测试桩 — ChatService 必须显式 Setup 历史(Loose mock 默认 null 会 NRE)</summary>
    private static (CompactCommand Command, ChatCommandContext Context, Mock<IChatService> Chat, Mock<IChatContextManager>? ContextManager) Arrange(
        string arguments = "",
        IReadOnlyList<ApiMessageRecord>? history = null,
        Mock<IChatContextManager>? contextManager = null) {
        var chat = new Mock<IChatService>();
        chat.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(history ?? []);
        chat.Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RawSummary);

        var services = new ServiceCollection();
        if (contextManager is not null) services.AddSingleton(contextManager.Object);
        var commandServices = new CommandServices {
            ChatService = chat.Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
        };
        var context = new ChatCommandContext {
            Arguments = arguments,
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };
        return (new CompactCommand(), context, chat, contextManager);
    }

    private static IReadOnlyList<ApiMessageRecord> CreateHistory() => [
        new() { Role = "user", Content = new string('a', 40) },
        new() { Role = "assistant", Content = new string('b', 80) },
        new() { Role = "user", Content = new string('c', 40) },
    ];

    [Fact]
    public async Task Execute_EmptyHistory_ShowsNoContentMessage() {
        var (cmd, context, chat, _) = Arrange(history: []);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("正在压缩上下文...");
        output.Should().Contain("没有对话内容可压缩");
        chat.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "空历史不应调用摘要生成");
        chat.Verify(c => c.CompactHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "空历史不应压缩");
    }

    [Fact]
    public async Task Execute_WithHistory_CompactsAndRendersSummary() {
        var (cmd, context, chat, _) = Arrange(history: CreateHistory());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("正在压缩上下文...");
        output.Should().Contain("上下文已压缩");
        output.Should().Contain("消息: 3 条已摘要");
        output.Should().Contain("Token: 40 →", "160 字符 / 4 = 40 原始 Token 应在渲染中显示");
        chat.Verify(c => c.CompactHistoryAsync(FormattedSummary, It.IsAny<CancellationToken>()), Times.Once,
            "摘要应经 FormatCompactSummary 提取 summary 标签后写入压缩历史");
        chat.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_CustomInstructions_AppendedToSummaryPrompt() {
        string? capturedPrompt = null;
        var (cmd, context, chat, _) = Arrange(arguments: "聚焦代码变更要点", history: CreateHistory());
        chat.Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((prompt, _) => capturedPrompt = prompt)
            .ReturnsAsync(RawSummary);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        capturedPrompt.Should().NotBeNull();
        Assert.NotNull(capturedPrompt);
        capturedPrompt.Should().Contain("额外说明：");
        capturedPrompt.Should().Contain("聚焦代码变更要点",
            "自定义摘要指令应追加到压缩 prompt 末尾(对齐 TS customInstructions)");
    }

    [Fact]
    public async Task Execute_SummaryGenerationFails_StillCompactsWithFallback() {
        var (cmd, context, chat, _) = Arrange(history: CreateHistory());
        chat.Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("网络错误"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("生成摘要失败: 网络错误");
        chat.Verify(c => c.CompactHistoryAsync("[无法生成摘要]", It.IsAny<CancellationToken>()), Times.Once,
            "摘要生成失败应回退占位摘要继续压缩而非中断命令");
        output.Should().Contain("上下文已压缩");
    }

    [Fact]
    public async Task Execute_CompactHistoryPromptTooLong_RewindsAndRetries() {
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(m => m.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 1));
        var (cmd, context, chat, ctxMgr) = Arrange(history: CreateHistory(), contextManager: contextManager);
        chat.SetupSequence(c => c.CompactHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("API Error: prompt_too_long"))
            .Returns(Task.CompletedTask);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("上下文过长，自动精简后重试... (1/3)",
            "PTL 异常应回滚最近一轮后重试");
        Assert.NotNull(ctxMgr);
        ctxMgr.Verify(m => m.RewindLastTurnAsync(It.IsAny<CancellationToken>()), Times.Once);
        chat.Verify(c => c.CompactHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2),
            "第一次 PTL 失败 + 重试成功");
        output.Should().Contain("上下文已压缩");
    }

    /// <summary>生产 bug 登记(TASK065 ai 域): PTL 重试耗尽(attempt=2)时 catch 条件
    /// <c>attempt &lt; maxPtRetries - 1</c> 不再满足,第 3 次 PTL 异常从 ExecuteAsync 直接冒泡
    /// 传播 REPL — "最终失败"兜底块只覆盖 Rewind 零移除 break 路径,不覆盖第三次异常路径。
    /// 本测试为现状快照,修复后应翻转为"走最终兜底 + HandleError 渲染"断言。</summary>
    [Fact]
    public async Task Execute_PtlRetriesExhausted_ThirdAttemptExceptionEscapes() {
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(m => m.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 1));
        var (cmd, context, chat, _) = Arrange(history: CreateHistory(), contextManager: contextManager);
        chat.SetupSequence(c => c.CompactHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("prompt too long"))
            .ThrowsAsync(new InvalidOperationException("prompt too long"))
            .ThrowsAsync(new InvalidOperationException("prompt too long"));

        var act = () => CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("prompt too long");
        contextManager.Verify(m => m.RewindLastTurnAsync(It.IsAny<CancellationToken>()), Times.Exactly(2),
            "前两次 PTL 各回滚一次,第三次异常不再捕获");
    }

    [Fact]
    public async Task Execute_PtlRewindRemovesNothing_FallsToFinalAttempt() {
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(m => m.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 0, 3));
        var (cmd, context, chat, _) = Arrange(history: CreateHistory(), contextManager: contextManager);
        chat.SetupSequence(c => c.CompactHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("prompt too long"))
            .Returns(Task.CompletedTask);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().NotContain("上下文过长，自动精简后重试...",
            "回滚零移除应直接 break 走最终兜底,而非输出重试提示");
        chat.Verify(c => c.CompactHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2),
            "回滚零移除 break 后最终兜底应再次尝试压缩");
        contextManager.Verify(m => m.RewindLastTurnAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>internal 静态指标契约 — 消息数与字符/4 估算 Token 直测(InternalsVisibleTo)</summary>
    [Fact]
    public void CalculateOriginalMetrics_ComputesCountAndEstimatedTokens() {
        var history = CreateHistory();

        var (count, tokens) = CompactCommand.CalculateOriginalMetrics(history);

        count.Should().Be(3);
        tokens.Should().Be(40, "160 字符 / 4 = 40");
    }

    [Fact]
    public void CalculateOriginalMetrics_EmptyHistory_ReturnsZero() {
        var (count, tokens) = CompactCommand.CalculateOriginalMetrics([]);

        count.Should().Be(0);
        tokens.Should().Be(0);
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new CompactCommand();
        cmd.Name.Should().Be("compact");
        cmd.Usage.Should().StartWith("/compact");
        cmd.Aliases.Should().Contain("comp");
        cmd.ArgumentHint.Should().Be("<optional custom summarization instructions>");
        cmd.ExposeToMcp.Should().BeTrue("compact 声明 ExposeToMcp 供 MCP 调用");
    }
}
