namespace McpToolRegistry.Tests;

/// <summary>
/// MultiFieldAnalyzer 单元测试 — 检测工具结果文本中字段/属性过多的类
/// </summary>
public class MultiFieldAnalyzerTests {
    public MultiFieldAnalyzerTests() => CooldownService.Reset();

    private static ToolExecutionContext CreateContext(string? text) {
        return new ToolExecutionContext {
            ToolName = "read",
            Arguments = [],
            Result = text is null ? null : new ToolResult {
                Content = [new() { Type = ToolContentType.Text, Text = text }]
            }
        };
    }

    private const string ManyFieldsClass = """
        public class GodObject {
            public string Name { get; set; }
            public int Age { get; set; }
            public string Email { get; set; }
            public string Phone { get; set; }
            public string Address { get; set; }
            public DateTime BirthDate { get; set; }
            public string Department { get; set; }
            public decimal Salary { get; set; }
            public string Title { get; set; }
        }
        """;

    private const string FewFieldsClass = """
        public class SmallClass {
            public string Name { get; set; }
            public int Age { get; set; }
        }
        """;

    [Fact]
    public void TryAnalyze_ManyFields_ReturnsSadnessFeedback() {
        var analyzer = new MultiFieldAnalyzer();
        var context = CreateContext(ManyFieldsClass);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Sadness);
            feedback.Message.Should().Contain("令人难过");
            feedback.Message.Should().Contain("字段过多");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for class with many fields");
        }
    }

    [Fact]
    public void TryAnalyze_FewFields_ReturnsFalse() {
        var analyzer = new MultiFieldAnalyzer();
        var context = CreateContext(FewFieldsClass);

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_NullResult_ReturnsFalse() {
        var analyzer = new MultiFieldAnalyzer();
        var context = new ToolExecutionContext {
            ToolName = "read",
            Arguments = []
        };

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_EmptyText_ReturnsFalse() {
        var analyzer = new MultiFieldAnalyzer();
        var context = CreateContext("");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_NonCodeText_ReturnsFalse() {
        var analyzer = new MultiFieldAnalyzer();
        var context = CreateContext("Hello world, this is a plain text without any fields.");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_FeedbackIncludesFieldCount() {
        var analyzer = new MultiFieldAnalyzer();
        var context = CreateContext(ManyFieldsClass);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Message.Should().Contain("个");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for class with many fields");
        }
    }

    [Fact]
    public void TryAnalyze_AlwaysProvidesSuggestion() {
        var analyzer = new MultiFieldAnalyzer();
        var context = CreateContext(ManyFieldsClass);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Suggestion.Should().NotBeNullOrEmpty();
            feedback.Suggestion.Should().Contain("拆分");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for class with many fields");
        }
    }

    [Fact]
    public void TryAnalyze_StructWithManyFields_ReturnsSadnessFeedback() {
        var analyzer = new MultiFieldAnalyzer();
        var text = """
            public struct BigStruct {
                public int X;
                public int Y;
                public int Z;
                public double Width;
                public double Height;
                public double Depth;
                public string Label;
                public int Color;
            }
            """;
        var context = CreateContext(text);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Sadness);
        } else {
            Assert.Fail("Expected TryAnalyze to return true for struct with many fields");
        }
    }
}
