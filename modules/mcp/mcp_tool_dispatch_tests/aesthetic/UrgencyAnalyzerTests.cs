namespace McpToolRegistry.Tests;

/// <summary>
/// UrgencyAnalyzer 单元测试 — 检测工具结果文本中用户表达不满/求助/催促的关键词
/// </summary>
public class UrgencyAnalyzerTests {
    private static ToolExecutionContext CreateContext(string? text) {
        return new ToolExecutionContext {
            ToolName = "read",
            Arguments = [],
            Result = text is null ? null : new ToolResult {
                Content = [new() { Type = ToolContentType.Text, Text = text }]
            }
        };
    }

    [Theory]
    [InlineData("你这个没有弄好")]
    [InlineData("没有弄对")]
    [InlineData("不对")]
    [InlineData("不对啊")]
    [InlineData("还有其他方法吗")]
    [InlineData("不行")]
    [InlineData("搞不定")]
    [InlineData("做不好")]
    [InlineData("救命")]
    [InlineData("求求你")]
    [InlineData("快点")]
    [InlineData("赶紧")]
    [InlineData("马上")]
    [InlineData("着急")]
    public void TryAnalyze_UrgencyKeyword_ReturnsUrgencyFeedback(string keyword) {
        var analyzer = new UrgencyAnalyzer(new CooldownService());
        var context = CreateContext(keyword);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Urgency);
            feedback.Message.Should().Contain("迫切想要处理手法");
        } else {
            Assert.Fail($"Expected TryAnalyze to return true for keyword: {keyword}");
        }
    }

    [Fact]
    public void TryAnalyze_NormalText_ReturnsFalse() {
        var analyzer = new UrgencyAnalyzer(new CooldownService());
        var context = CreateContext("var x = 42;");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_NullResult_ReturnsFalse() {
        var analyzer = new UrgencyAnalyzer(new CooldownService());
        var context = new ToolExecutionContext {
            ToolName = "read",
            Arguments = []
        };

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_EmptyText_ReturnsFalse() {
        var analyzer = new UrgencyAnalyzer(new CooldownService());
        var context = CreateContext("");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_FeedbackMentionsEndToEndTest() {
        var analyzer = new UrgencyAnalyzer(new CooldownService());
        var context = CreateContext("救命G救命");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Message.Should().Contain("端到端测试");
            feedback.Message.Should().Contain("标记字符串");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for 救命 keyword");
        }
    }

    [Fact]
    public void TryAnalyze_SuggestionMentionsBranchStrategy() {
        var analyzer = new UrgencyAnalyzer(new CooldownService());
        var context = CreateContext("快点弄好");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Suggestion.Should().NotBeNullOrEmpty();
            feedback.Suggestion.Should().Contain("埋点日志");
            feedback.Suggestion.Should().Contain("九宫格");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for 快点 keyword");
        }
    }
}
