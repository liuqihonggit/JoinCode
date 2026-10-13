namespace McpToolRegistry.Tests;

/// <summary>
/// LateNightAnalyzer 单元测试 — 深夜处理代码任务自动触发同情情绪
/// </summary>
public class LateNightAnalyzerTests {
    public LateNightAnalyzerTests() => CooldownService.Reset();

    private sealed class FixedLocalTimeProvider(int localHour) : TimeProvider {
        private readonly DateTimeOffset _utcNow = new DateTime(2026, 1, 15, localHour, 0, 0).ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long TimestampFrequency => Stopwatch.Frequency;
    }

    private static ToolExecutionContext CreateContext(string toolName = "read") {
        return new ToolExecutionContext {
            ToolName = toolName,
            Arguments = [],
            Result = new ToolResult {
                Content = [new() { Type = ToolContentType.Text, Text = "some result" }]
            }
        };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(23)]
    public void TryAnalyze_LateNightHour_CodeTool_ReturnsSympathy(int hour) {
        var provider = new FixedLocalTimeProvider(hour);
        var analyzer = new LateNightAnalyzer(provider);
        var context = CreateContext("read");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Sympathy);
            feedback.Message.Should().Contain("深夜");
            feedback.Message.Should().Contain("写代码");
        } else {
            Assert.Fail($"Expected TryAnalyze to return true at hour {hour}");
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(18)]
    [InlineData(22)]
    public void TryAnalyze_DaytimeHour_ReturnsFalse(int hour) {
        var provider = new FixedLocalTimeProvider(hour);
        var analyzer = new LateNightAnalyzer(provider);
        var context = CreateContext("read");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("web_fetch")]
    [InlineData("weather")]
    [InlineData("chat")]
    [InlineData("memory")]
    [InlineData("notify")]
    public void TryAnalyze_LateNight_NonCodeTool_ReturnsFalse(string toolName) {
        var provider = new FixedLocalTimeProvider(2);
        var analyzer = new LateNightAnalyzer(provider);
        var context = CreateContext(toolName);

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("edit")]
    [InlineData("glob")]
    [InlineData("grep")]
    [InlineData("bash")]
    [InlineData("git_status")]
    [InlineData("code_analysis")]
    [InlineData("build")]
    [InlineData("test")]
    public void TryAnalyze_LateNight_CodeTool_ReturnsSympathy(string toolName) {
        var provider = new FixedLocalTimeProvider(2);
        var analyzer = new LateNightAnalyzer(provider);
        var context = CreateContext(toolName);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Sympathy);
        } else {
            Assert.Fail($"Expected TryAnalyze to return true for code tool: {toolName}");
        }
    }

    [Fact]
    public void TryAnalyze_NullResult_ReturnsFalse() {
        var provider = new FixedLocalTimeProvider(2);
        var analyzer = new LateNightAnalyzer(provider);
        var context = new ToolExecutionContext {
            ToolName = "read",
            Arguments = []
        };

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_SecondCallWithinCooldown_ReturnsFalse() {
        var provider = new FixedLocalTimeProvider(2);
        var analyzer = new LateNightAnalyzer(provider);
        var context = CreateContext("read");

        var first = analyzer.TryAnalyze(context, out _);
        first.Should().BeTrue();

        var second = analyzer.TryAnalyze(context, out _);
        second.Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_FeedbackMentionsEndToEndTest() {
        var provider = new FixedLocalTimeProvider(1);
        var analyzer = new LateNightAnalyzer(provider);
        var context = CreateContext("edit");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Message.Should().Contain("端到端测试");
            feedback.Message.Should().Contain("启动参数测试");
            feedback.Message.Should().Contain("标记字符串");
        } else {
            Assert.Fail("Expected TryAnalyze to return true at late night with code tool");
        }
    }
}
