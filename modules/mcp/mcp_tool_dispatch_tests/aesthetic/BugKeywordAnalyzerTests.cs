namespace McpToolRegistry.Tests;

/// <summary>
/// BugKeywordAnalyzer 单元测试 — 检测工具结果文本中的 BUG/FIXME/HACK/WORKAROUND/XXX 关键字
/// </summary>
public class BugKeywordAnalyzerTests {
    private static ToolExecutionContext CreateContext(string? text) {
        return new ToolExecutionContext {
            ToolName = "read",
            Arguments = [],
            Result = text is null ? null : new ToolResult {
                Content = [new() { Type = ToolContentType.Text, Text = text }]
            }
        };
    }

    [Fact]
    public void TryAnalyze_ResultContainsBugKeyword_ReturnsAlertFeedback() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("// BUG: this is broken");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Alert);
            feedback.Message.Should().Contain("BUG");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for BUG keyword");
        }
    }

    [Fact]
    public void TryAnalyze_ResultContainsFixmeKeyword_ReturnsAlertFeedback() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("// FIXME: need to fix this");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Alert);
            feedback.Message.Should().Contain("FIXME");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for FIXME keyword");
        }
    }

    [Fact]
    public void TryAnalyze_ResultContainsHackKeyword_ReturnsAlertFeedback() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("// HACK: temporary workaround");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Alert);
            feedback.Message.Should().Contain("HACK");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for HACK keyword");
        }
    }

    [Fact]
    public void TryAnalyze_ResultContainsWorkaroundKeyword_ReturnsAlertFeedback() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("// WORKAROUND: bypass issue #123");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Alert);
            feedback.Message.Should().Contain("WORKAROUND");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for WORKAROUND keyword");
        }
    }

    [Fact]
    public void TryAnalyze_ResultContainsXxxKeyword_ReturnsAlertFeedback() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("// XXX: not sure about this");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Alert);
            feedback.Message.Should().Contain("XXX");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for XXX keyword");
        }
    }

    [Fact]
    public void TryAnalyze_ResultWithoutKeywords_ReturnsFalse() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("var x = 42;");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_NullResult_ReturnsFalse() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = new ToolExecutionContext {
            ToolName = "read",
            Arguments = []
        };

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_EmptyText_ReturnsFalse() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_MultipleKeywords_FeedbackContainsAllDistinctKeywords() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("// BUG: broken\n// FIXME: also broken\n// HACK: temp");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Message.Should().Contain("BUG");
            feedback.Message.Should().Contain("FIXME");
            feedback.Message.Should().Contain("HACK");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for multiple keywords");
        }
    }

    [Fact]
    public void TryAnalyze_DuplicateKeywords_FeedbackDeduplicates() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("// BUG: first\n// BUG: second\n// BUG: third");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Message.Should().Contain("BUG");
            var bugCount = feedback.Message.Split("BUG").Length - 1;
            bugCount.Should().Be(1);
        } else {
            Assert.Fail("Expected TryAnalyze to return true for BUG keyword");
        }
    }

    [Fact]
    public void TryAnalyze_KeywordInWordBoundary_DoesNotMatch() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("var debugMode = true;");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_AlwaysProvidesSuggestion() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        var context = CreateContext("// BUG: broken");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Suggestion.Should().NotBeNullOrEmpty();
            feedback.Suggestion.Should().Contain("立即调查");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for BUG keyword");
        }
    }
}
