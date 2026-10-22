namespace Composition.Tests.Commands;

/// <summary>
/// /extra-usage 命令单元测试 — 覆盖今日/累计统计渲染(N0/F4 格式化)/空统计全零/
/// 追踪器缺失降级/统计异常收敛/元数据一致性
/// <para>UsageTracker 用 Moq 桩经 CommandServices.UsageTracker 可选字段注入</para>
/// </summary>
[Collection("command-output")]
public sealed class ExtraUsageCommandTest {
    private static TokenUsageStatistics CreateStats(
        int requests = 5,
        long input = 1200,
        long output = 800,
        decimal cost = 0.05m) => new() {
        TotalRequests = requests,
        TotalInputTokens = input,
        TotalOutputTokens = output,
        TotalCostUsd = cost,
    };

    private static (ExtraUsageCommand Command, ChatCommandContext Context, Mock<IUsageTracker> Tracker) Arrange(
        string arguments = "",
        Mock<IUsageTracker>? tracker = null) {
        var usageTracker = tracker ?? new Mock<IUsageTracker>();
        var services = CommandTestHarness.CreateServices(usageTracker: usageTracker.Object);
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new ExtraUsageCommand(), context, usageTracker);
    }

    [Fact]
    public async Task Execute_WithTracker_RendersTodayAndTotalStats() {
        var tracker = new Mock<IUsageTracker>();
        tracker.Setup(t => t.GetTodayStatistics()).Returns(CreateStats());
        tracker.Setup(t => t.GetTotalStatistics()).Returns(CreateStats(requests: 42, input: 999999, output: 888888, cost: 12.3456m));
        var (cmd, context, _) = Arrange(tracker: tracker);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("额外用量:");
        output.Should().Contain("  今日统计:");
        output.Should().Contain("    请求数: 5");
        output.Should().Contain("    输入 Token: 1,200", "Token 数量应 N0 千分位格式化");
        output.Should().Contain("    输出 Token: 800");
        output.Should().Contain("    估算成本: $0.0500", "成本应 F4 四位小数格式化");
        output.Should().Contain("  累计统计:");
        output.Should().Contain("    总请求数: 42");
        output.Should().Contain("    总输入 Token: 999,999");
        output.Should().Contain("    总输出 Token: 888,888");
        output.Should().Contain("    总估算成本: $12.3456");
        tracker.Verify(t => t.GetTodayStatistics(), Times.Once);
        tracker.Verify(t => t.GetTotalStatistics(), Times.Once);
    }

    [Fact]
    public async Task Execute_EmptyStats_RendersZeroValues() {
        var tracker = new Mock<IUsageTracker>();
        tracker.Setup(t => t.GetTodayStatistics()).Returns(new TokenUsageStatistics());
        tracker.Setup(t => t.GetTotalStatistics()).Returns(new TokenUsageStatistics());
        var (cmd, context, _) = Arrange(tracker: tracker);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("请求数: 0", "空统计(全零)应渲染面板而非空输出或崩溃");
        output.Should().Contain("估算成本: $0.0000");
    }

    [Fact]
    public async Task Execute_WithoutTracker_ShowsUnavailable() {
        var services = CommandTestHarness.CreateServices(usageTracker: null);
        var context = CommandTestHarness.CreateContext(services);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ExtraUsageCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("追踪器缺失应降级提示而非崩溃");
        output.Should().Contain("额外用量:");
        output.Should().Contain("  用量追踪器未初始化");
    }

    [Fact]
    public async Task Execute_StatsFailure_ConvergesToErrorHint() {
        var tracker = new Mock<IUsageTracker>();
        tracker.Setup(t => t.GetTodayStatistics()).Throws(new InvalidOperationException("disk locked"));
        var (cmd, context, _) = Arrange(tracker: tracker);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("统计异常应收敛为错误提示而非向上抛出");
        output.Should().Contain("获取用量数据失败: disk locked");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ExtraUsageCommand();
        cmd.Name.Should().Be("extra-usage");
        cmd.Usage.Should().Be("/extra-usage");
        cmd.Description.Should().Contain("额外用量");
        cmd.IsHidden.Should().BeTrue("extra-usage 为隐藏命令");
    }
}
