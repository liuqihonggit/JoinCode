// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace Composition.Tests.Commands;

/// <summary>
/// /insights 命令单元测试 — 覆盖 stats 模式(扫描器缺失回退当前会话/空会话/有会话聚合渲染/扫描异常回退)/
/// deep 与 report 模式(扫描器缺失降级)/AI 基础洞察(发送 prompt 到 ChatService)/
/// s/d/r 短别名等价/元数据一致性
/// <para>InsightSessionScanner 与 FacetCacheService 用 Moq 桩经 DI 注入;
/// ChatService 为 CommandServices 必填字段,自建服务包注入 mock(对齐 MemoryCommandTest 形态)</para>
/// </summary>
[Collection("command-output")]
public sealed class InsightsCommandTest {
    private static (InsightsCommand Command, ChatCommandContext Context, Mock<IChatService> Chat) Arrange(
        string arguments = "",
        Mock<IInsightSessionScanner>? scanner = null,
        Mock<IFacetCacheService>? facetCache = null,
        Mock<IChatService>? chat = null) {
        var chatService = chat ?? new Mock<IChatService>();
        chatService.Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        var di = new ServiceCollection();
        if (scanner is not null) di.AddSingleton(scanner.Object);
        if (facetCache is not null) di.AddSingleton(facetCache.Object);
        var commandServices = new CommandServices {
            ChatService = chatService.Object,
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = di.BuildServiceProvider(),
        };
        var context = new ChatCommandContext {
            Arguments = arguments,
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };
        return (new InsightsCommand(), context, chatService);
    }

    private static Mock<IInsightSessionScanner> ScannerWithSessions(params InsightSessionMeta[] sessions) {
        var scanner = new Mock<IInsightSessionScanner>();
        scanner.Setup(s => s.ScanAllSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions.ToList());
        return scanner;
    }

    private static InsightSessionMeta Session(string id = "session-1") => new() {
        SessionId = id,
        ProjectPath = "D:\\project\\w2",
        StartTime = DateTime.UtcNow.AddDays(-1),
        DurationMinutes = 42,
        UserMessageCount = 10,
        AssistantMessageCount = 12,
        InputTokens = 1000,
        OutputTokens = 2000,
        FirstPrompt = "写一个单元测试",
    };

    [Fact]
    public async Task Execute_Stats_WithoutScanner_FallsBackToCurrentSession() {
        var (cmd, context, _) = Arrange(arguments: "stats", scanner: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前会话统计:");
        output.Should().Contain("  会话时长:");
        output.Should().Contain("  会话ID: test-session");
        output.Should().Contain("  (成本追踪器不可用)", "CostTracker 缺失应降级提示");
        output.Should().Contain("使用 /insights (不带stats) 获取AI生成的洞察分析",
            "回退模式应引导用户使用 AI 洞察");
    }

    [Fact]
    public async Task Execute_Stats_EmptySessions_ShowsNoRecordsHint() {
        var scanner = ScannerWithSessions();
        var (cmd, context, _) = Arrange(arguments: "stats", scanner: scanner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("正在扫描会话文件...");
        output.Should().Contain("未找到会话记录。");
        output.Should().Contain("使用 /insights (不带stats) 获取AI生成的洞察分析");
    }

    [Fact]
    public async Task Execute_Stats_WithSessions_RendersAggregatedReport() {
        var scanner = ScannerWithSessions(Session(), Session("session-2"));
        var (cmd, context, _) = Arrange(arguments: "stats", scanner: scanner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("正在扫描会话文件...");
        output.Should().Contain("会话统计");
        output.Should().Contain("  总会话数: 2", "应渲染聚合后的会话总数");
        output.Should().Contain("  总消息数: 44", "消息数 = (10+12)*2");
        output.Should().Contain("  输入 Token: 2,000");
        output.Should().Contain("  输出 Token: 4,000");
        output.Should().Contain("  活跃天数: 1");
    }

    [Fact]
    public async Task Execute_Stats_ScanFailure_FallsBackToCurrentSession() {
        var scanner = new Mock<IInsightSessionScanner>();
        scanner.Setup(s => s.ScanAllSessionsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk locked"));
        var (cmd, context, _) = Arrange(arguments: "stats", scanner: scanner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("扫描异常应收敛回退而非向上抛出");
        output.Should().Contain("扫描会话失败: disk locked");
        output.Should().Contain("当前会话统计:", "扫描失败应回退当前会话统计");
    }

    [Theory]
    [InlineData("deep")]
    [InlineData("d")]
    public async Task Execute_Deep_WithoutScanner_ShowsUnavailable(string arguments) {
        var (cmd, context, _) = Arrange(arguments: arguments, scanner: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("会话扫描服务不可用，无法生成深度洞察。");
        output.Should().Contain("使用 /insights (不带deep) 获取基础AI洞察分析",
            "降级提示应引导用户回退基础洞察");
    }

    [Fact]
    public async Task Execute_Deep_EmptySessions_ShowsNoRecords() {
        var scanner = ScannerWithSessions();
        var (cmd, context, chat) = Arrange(arguments: "deep", scanner: scanner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("正在扫描会话文件...");
        output.Should().Contain("未找到会话记录。");
        chat.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "无会话时不应调用 LLM");
    }

    [Fact]
    public async Task Execute_Deep_WithSessions_SendsDeepPromptToChat() {
        var scanner = ScannerWithSessions(Session());
        var (cmd, context, chat) = Arrange(arguments: "deep", scanner: scanner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("找到 1 个会话");
        output.Should().Contain("正在提取会话 Facet...");
        output.Should().Contain("正在生成深度洞察...");
        chat.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "深度洞察应把数据上下文发给 ChatService 生成");
    }

    [Theory]
    [InlineData("report")]
    [InlineData("r")]
    public async Task Execute_Report_WithoutScanner_ShowsUnavailable(string arguments) {
        var (cmd, context, _) = Arrange(arguments: arguments, scanner: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("会话扫描服务不可用，无法生成报告。");
    }

    [Fact]
    public async Task Execute_Report_WithSessions_GeneratesHtmlAndStats() {
        var scanner = ScannerWithSessions(Session());
        var (cmd, context, _) = Arrange(arguments: "report", scanner: scanner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("找到 1 个会话");
        output.Should().Contain("正在生成洞察文本...");
        output.Should().Contain("正在生成 HTML 报告...");
        output.Should().Contain("HTML 报告已生成:");
        output.Should().Contain("report.html", "报告应落盘到 usage-data/report.html");
        output.Should().Contain("会话统计", "报告完成后应同时输出终端统计");
    }

    [Fact]
    public async Task Execute_DefaultMode_SendsCurrentSessionPromptToChat() {
        var (cmd, context, chat) = Arrange(arguments: "", scanner: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("正在生成会话洞察...");
        chat.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "基础 AI 洞察应发送 prompt 给 ChatService");
    }

    [Fact]
    public async Task Execute_DefaultMode_WithSessions_SendsCrossSessionPrompt() {
        var scanner = ScannerWithSessions(Session());
        var (cmd, context, chat) = Arrange(arguments: "", scanner: scanner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("正在分析会话数据...");
        output.Should().Contain("正在生成会话洞察...");
        chat.Verify(c => c.SendMessageAsync(
            It.Is<string>(p => p.Contains("Sessions: 1") && p.Contains("Total messages: 22")),
            It.IsAny<CancellationToken>()), Times.Once,
            "有会话时应以跨会话聚合数据作为 prompt 上下文");
    }

    [Fact]
    public async Task Execute_SAlias_EquivalentToStats() {
        var (cmd, context, _) = Arrange(arguments: "s", scanner: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前会话统计:", "s 为 stats 的短别名");
    }

    [Fact]
    public async Task Execute_UnrecognizedMode_FallsBackToAiInsights() {
        var (cmd, context, chat) = Arrange(arguments: "bogus", scanner: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("正在生成会话洞察...", "未识别参数应回退基础 AI 洞察模式");
        chat.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new InsightsCommand();
        cmd.Name.Should().Be("insights");
        cmd.Usage.Should().Be("/insights [stats|deep|report]");
        cmd.Description.Should().Contain("洞察");
        cmd.ArgumentHint.Should().Be("[stats|deep|report]");
    }
}
