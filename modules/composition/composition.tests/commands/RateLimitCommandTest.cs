namespace Composition.Tests.Commands;

/// <summary>
/// /rate-limit-options 命令单元测试 — 覆盖空参/show 快照渲染(N0/日期格式/null 降级)/
/// 未知操作引导/追踪器缺失降级/大小写不敏感/元数据一致性
/// <para>RateLimitTracker 用 Moq 桩经 CommandServices.RateLimitTracker 可选字段注入</para>
/// </summary>
[Collection("command-output")]
public sealed class RateLimitCommandTest {
    private static (RateLimitCommand Command, ChatCommandContext Context, Mock<IRateLimitTracker> Tracker) Arrange(
        string arguments = "",
        Mock<IRateLimitTracker>? tracker = null) {
        var rateLimitTracker = tracker ?? new Mock<IRateLimitTracker>();
        var services = CommandTestHarness.CreateServices(rateLimitTracker: rateLimitTracker.Object);
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new RateLimitCommand(), context, rateLimitTracker);
    }

    private static RateLimitSnapshot CreateSnapshot() => new() {
        RequestLimit = 1000,
        RequestRemaining = 999,
        RequestResetsAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        TokenLimit = 2000000,
        TokenRemaining = 1234567,
        TokenResetsAt = new DateTime(2026, 1, 1, 12, 30, 0, DateTimeKind.Utc),
        CapturedAt = new DateTime(2026, 1, 1, 8, 15, 30, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Execute_NoSnapshot_ShowsEmptyHint() {
        var tracker = new Mock<IRateLimitTracker>();
        tracker.Setup(t => t.GetLatestSnapshot()).Returns((RateLimitSnapshot?)null);
        var (cmd, context, _) = Arrange(tracker: tracker);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("速率限制:");
        output.Should().Contain("  暂无速率限制数据");
        output.Should().Contain("  数据将在下次 API 请求后更新", "空快照应引导用户等待下次请求");
    }

    [Fact]
    public async Task Execute_WithSnapshot_RendersLimitsAndResets() {
        var tracker = new Mock<IRateLimitTracker>();
        tracker.Setup(t => t.GetLatestSnapshot()).Returns(CreateSnapshot());
        var (cmd, context, _) = Arrange(arguments: "show", tracker: tracker);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("  请求限制: 1,000", "限制数值应 N0 千分位格式化");
        output.Should().Contain("  请求剩余: 999");
        output.Should().Contain("  请求重置: 2026-01-01 00:00:00", "重置时间应 yyyy-MM-dd HH:mm:ss 格式化");
        output.Should().Contain("  Token 限制: 2,000,000");
        output.Should().Contain("  Token 剩余: 1,234,567");
        output.Should().Contain("  Token 重置: 2026-01-01 12:30:00");
        output.Should().Contain("  捕获时间: 08:15:30", "捕获时间应 HH:mm:ss 短格式");
    }

    [Fact]
    public async Task Execute_NullFields_RenderFallbackLabels() {
        var tracker = new Mock<IRateLimitTracker>();
        tracker.Setup(t => t.GetLatestSnapshot()).Returns(new RateLimitSnapshot());
        var (cmd, context, _) = Arrange(tracker: tracker);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("请求限制: 无限制", "null 限制应显示无限制而非空值");
        output.Should().Contain("请求重置: 未知", "null 重置时间应显示未知");
        output.Should().Contain("Token 限制: 无限制");
        output.Should().Contain("Token 重置: 未知");
    }

    [Theory]
    [InlineData("")]
    [InlineData("show")]
    [InlineData("SHOW")]
    public async Task Execute_ShowVariants_AcceptedCaseInsensitively(string arguments) {
        var tracker = new Mock<IRateLimitTracker>();
        tracker.Setup(t => t.GetLatestSnapshot()).Returns((RateLimitSnapshot?)null);
        var (cmd, context, _) = Arrange(arguments: arguments, tracker: tracker);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("速率限制:", "空参/show/SHOW 应等价显示快照（大小写不敏感）");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsErrorAndSupportedAction() {
        var (cmd, context, _) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知操作: bogus");
        output.Should().Contain("支持: show", "错误提示应列出支持的操作引导用户");
    }

    [Fact]
    public async Task Execute_WithoutTracker_ShowsUnavailable() {
        var services = CommandTestHarness.CreateServices(rateLimitTracker: null);
        var context = CommandTestHarness.CreateContext(services);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new RateLimitCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("追踪器缺失应降级提示而非崩溃");
        output.Should().Contain("速率限制:");
        output.Should().Contain("  速率限制追踪器未初始化");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new RateLimitCommand();
        cmd.Name.Should().Be("rate-limit-options");
        cmd.Usage.Should().Be("/rate-limit-options [show]");
        cmd.Description.Should().Contain("速率限制");
        // TASK071 命令技能-① — "rate-limit" 别名归属 UsageCommand（双声明冲突裁决）；
// TS 基准 rate-limit-options 无别名（且本命令 IsHidden），别名删除后此断言随语义移除
cmd.Aliases.Should().NotContain("rate-limit", "别名冲突裁决：rate-limit 归属 UsageCommand");
        cmd.IsHidden.Should().BeTrue("rate-limit-options 为隐藏命令");
    }
}
