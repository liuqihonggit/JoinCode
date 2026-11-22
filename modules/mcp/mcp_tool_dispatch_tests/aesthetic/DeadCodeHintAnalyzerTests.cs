namespace McpToolRegistry.Tests;

/// <summary>
/// DeadCodeHintAnalyzer 单元测试 — 检测工具结果文本中暗示死代码的关键词
/// </summary>
public class DeadCodeHintAnalyzerTests {
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
    public void TryAnalyze_UnreferencedKeyword_ReturnsConfusionFeedback() {
        var analyzer = new DeadCodeHintAnalyzer(new CooldownService());
        var context = CreateContext("Method 'Foo' is unreferenced.");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Confusion);
            feedback.Message.Should().Contain("死代码");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for unreferenced keyword");
        }
    }

    [Fact]
    public void TryAnalyze_NeverCalledKeyword_ReturnsConfusionFeedback() {
        var analyzer = new DeadCodeHintAnalyzer(new CooldownService());
        var context = CreateContext("Warning: method Bar is never called.");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Confusion);
        } else {
            Assert.Fail("Expected TryAnalyze to return true for never called keyword");
        }
    }

    [Fact]
    public void TryAnalyze_DeadCodeKeyword_ReturnsConfusionFeedback() {
        var analyzer = new DeadCodeHintAnalyzer(new CooldownService());
        var context = CreateContext("Detected dead code in module X.");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Confusion);
        } else {
            Assert.Fail("Expected TryAnalyze to return true for dead code keyword");
        }
    }

    [Fact]
    public void TryAnalyze_NoCallersKeyword_ReturnsConfusionFeedback() {
        var analyzer = new DeadCodeHintAnalyzer(new CooldownService());
        var context = CreateContext("Function has no callers in the codebase.");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Confusion);
        } else {
            Assert.Fail("Expected TryAnalyze to return true for no callers keyword");
        }
    }

    [Fact]
    public void TryAnalyze_OrphanedKeyword_ReturnsConfusionFeedback() {
        var analyzer = new DeadCodeHintAnalyzer(new CooldownService());
        var context = CreateContext("Found orphaned function definitions.");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Confusion);
        } else {
            Assert.Fail("Expected TryAnalyze to return true for orphaned keyword");
        }
    }

    [Fact]
    public void TryAnalyze_NormalText_ReturnsFalse() {
        var analyzer = new DeadCodeHintAnalyzer(new CooldownService());
        var context = CreateContext("var x = 42;");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_NullResult_ReturnsFalse() {
        var analyzer = new DeadCodeHintAnalyzer(new CooldownService());
        var context = new ToolExecutionContext {
            ToolName = "read",
            Arguments = []
        };

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_EmptyText_ReturnsFalse() {
        var analyzer = new DeadCodeHintAnalyzer(new CooldownService());
        var context = CreateContext("");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_AlwaysProvidesSuggestion() {
        var analyzer = new DeadCodeHintAnalyzer(new CooldownService());
        var context = CreateContext("Method is unreferenced.");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Suggestion.Should().NotBeNullOrEmpty();
            feedback.Suggestion.Should().Contain("GraphDetectDeadCode");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for unreferenced keyword");
        }
    }
}
