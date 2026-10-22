namespace Composition.Tests.Commands;

/// <summary>
/// /stats 命令单元测试 — 覆盖 scope 分发(today/--today 去横线/total/session/无效回退)/
/// UsageTracker 缺失降级默认数据/统计为空提示/缓存 Token 明细/跨会话模式(7d)三级:
/// 扫描器缺失/空会话/有会话渲染概览
/// </summary>
[Collection("command-output")]
public sealed class StatsCommandTest {
    private static (StatsCommand Command, ChatCommandContext Context, Mock<IUsageTracker> Tracker) Arrange(
        string arguments,
        Mock<IUsageTracker>? tracker = null,
        Mock<IInsightSessionScanner>? scanner = null) {
        var usageTracker = tracker ?? new Mock<IUsageTracker>();
        var services = CommandTestHarness.CreateServices(
            usageTracker: usageTracker.Object,
            configureDi: di => {
                if (scanner is not null) di.AddSingleton(scanner.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new StatsCommand(), context, usageTracker);
    }

    /// <summary>
    /// 配置统计桩返回 null — IUsageTracker 接口签名非空,但 StatsCommand 存在 stats is null
    /// 防御分支(契约与实现不一致的生产侧隐患),此处显式模拟 null 验证降级路径
    /// </summary>
    private static void SetupNullStats(Mock<IUsageTracker> tracker, System.Linq.Expressions.Expression<Func<IUsageTracker, TokenUsageStatistics>> getter) {
#pragma warning disable CS8603 // 边界桩:模拟接口契约外的 null 返回,验证命令侧 null 防御分支
        tracker.Setup(getter).Returns(() => null);
#pragma warning restore CS8603
    }


    private static TokenUsageStatistics CreateStats(
        long input = 1200,
        long output = 800,
        long cacheCreation = 0,
        long cacheRead = 0) => new() {
        TotalRequests = 5,
        TotalInputTokens = input,
        TotalOutputTokens = output,
        TotalCacheCreationTokens = cacheCreation,
        TotalCacheReadTokens = cacheRead,
        TotalCostUsd = 0.05m,
    };

    [Fact]
    public async Task Execute_WithoutUsageTracker_ShowsFallbackData() {
        var services = CommandTestHarness.CreateServices(usageTracker: null);
        var context = CommandTestHarness.CreateContext(services);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new StatsCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("使用量追踪服务不可用，显示默认数据", "追踪器缺失应降级显示默认数据");
    }

    [Fact]
    public async Task Execute_TodayWithoutStatistics_ShowsEmptyHint() {
        var tracker = new Mock<IUsageTracker>();
        SetupNullStats(tracker, t => t.GetTodayStatistics());
        var (cmd, context, _) = Arrange("", tracker: tracker);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        tracker.Verify(t => t.GetTodayStatistics(), Times.Once, "空参默认 today 范围");
        output.Should().Contain("暂无统计数据");
    }

    [Fact]
    public async Task Execute_TodayFlag_StripsLeadingDashes() {
        var tracker = new Mock<IUsageTracker>();
        SetupNullStats(tracker, t => t.GetTodayStatistics());
        var (cmd, context, _) = Arrange("--today", tracker: tracker);

        var (_, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        tracker.Verify(t => t.GetTodayStatistics(), Times.Once, "--today 应去除横线后等价 today");
    }

    [Fact]
    public async Task Execute_Total_ScopesToTotalStatistics() {
        var tracker = new Mock<IUsageTracker>();
        tracker.Setup(t => t.GetTotalStatistics()).Returns(CreateStats());
        var (cmd, context, _) = Arrange("total", tracker: tracker);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        tracker.Verify(t => t.GetTotalStatistics(), Times.Once, "total 应查询累计统计");
        tracker.Verify(t => t.GetTodayStatistics(), Times.Never, "total 不应触发今日统计查询");
    }

    [Fact]
    public async Task Execute_Session_ScopesToSessionStatisticsWithId() {
        var tracker = new Mock<IUsageTracker>();
        SetupNullStats(tracker, t => t.GetSessionStatistics(It.IsAny<string>()));
        var (cmd, context, _) = Arrange("session", tracker: tracker);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        tracker.Verify(t => t.GetSessionStatistics("test-session"), Times.Once,
            "session 范围应携带当前会话 ID 查询");
    }

    [Fact]
    public async Task Execute_InvalidScope_FallsBackToToday() {
        var tracker = new Mock<IUsageTracker>();
        SetupNullStats(tracker, t => t.GetTodayStatistics());
        var (cmd, context, _) = Arrange("bogus", tracker: tracker);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        tracker.Verify(t => t.GetTodayStatistics(), Times.Once, "无效 scope 应回退 today 范围");
        tracker.Verify(t => t.GetTotalStatistics(), Times.Never);
    }

    [Fact]
    public async Task Execute_TodayWithCacheTokens_ShowsCacheBreakdown() {
        var tracker = new Mock<IUsageTracker>();
        tracker.Setup(t => t.GetTodayStatistics())
            .Returns(CreateStats(cacheCreation: 5000, cacheRead: 12000));
        var (cmd, context, _) = Arrange("", tracker: tracker);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("缓存创建: 5,000");
        output.Should().Contain("缓存读取: 12,000", "缓存 Token 数量应格式化输出");
    }

    [Fact]
    public async Task Execute_SevenDays_WithoutScanner_ShowsUnavailable() {
        var (cmd, context, _) = Arrange("7d");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("会话扫描服务不可用，无法获取跨会话统计。");
        output.Should().Contain("使用 /stats --today 查看今日统计。",
            "错误提示应引导用户回退今日统计");
    }

    [Fact]
    public async Task Execute_SevenDays_NoSessions_ShowsEmptyHint() {
        var scanner = new Mock<IInsightSessionScanner>();
        scanner.Setup(s => s.ScanAllSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InsightSessionMeta>());
        var (cmd, context, _) = Arrange("7d", scanner: scanner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未找到会话记录。开始使用以解锁统计数据！");
    }

    [Fact]
    public async Task Execute_SevenDays_WithSessions_RendersCrossSessionOverview() {
        var scanner = new Mock<IInsightSessionScanner>();
        scanner.Setup(s => s.ScanAllSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InsightSessionMeta> {
                new() {
                    SessionId = "session-1",
                    ProjectPath = "D:\\project\\w2",
                    StartTime = DateTime.UtcNow.AddDays(-1),
                    DurationMinutes = 42,
                    UserMessageCount = 10,
                    AssistantMessageCount = 12,
                    InputTokens = 1000,
                    OutputTokens = 2000,
                },
            });
        var (cmd, context, _) = Arrange("7d", scanner: scanner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("正在扫描会话文件...");
        output.Should().Contain("Stats — Last 7 days", "7d 范围应渲染跨会话概览标题");
    }

    [Fact]
    public async Task Execute_ScannerFailure_ConvergesToErrorHint() {
        var scanner = new Mock<IInsightSessionScanner>();
        scanner.Setup(s => s.ScanAllSessionsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk locked"));
        var (cmd, context, _) = Arrange("30d", scanner: scanner);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("扫描会话失败: disk locked", "扫描异常应收敛为错误提示而非崩溃");
        output.Should().Contain("使用 /stats --today 查看今日统计。");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new StatsCommand();
        cmd.Name.Should().Be("stats");
        cmd.Usage.Should().StartWith("/stats");
        cmd.Aliases.Should().Contain("stat", "stat 为 /stats 的别名");
        cmd.ExposeToMcp.Should().BeTrue();
    }
}
