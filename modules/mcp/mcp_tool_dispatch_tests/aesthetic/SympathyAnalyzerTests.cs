namespace McpToolRegistry.Tests;

/// <summary>
/// SympathyAnalyzer 单元测试 — 检测六类困境信号锚点
/// </summary>
public class SympathyAnalyzerTests {
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
    [InlineData("妈妈患上了癌症")]
    [InlineData("diagnosed with cancer")]
    [InlineData("家人住院了")]
    [InlineData("明天要做手术")]
    [InlineData("开始化疗了")]
    [InlineData("脑梗住院")]
    public void TryAnalyze_DirectIllnessAnchor_ReturnsSympathy(string text) {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = CreateContext(text);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Sympathy);
            feedback.Message.Should().Contain("认真对待工作任务");
        } else {
            Assert.Fail($"Expected TryAnalyze to return true for: {text}");
        }
    }

    [Theory]
    [InlineData("真的撑不住了")]
    [InlineData("快要崩溃边缘了")]
    [InlineData("完全绝望了")]
    [InlineData("心力交瘁")]
    [InlineData("筋疲力尽")]
    [InlineData("哎，又出bug了")]
    [InlineData("唉，好累")]
    [InlineData("哎哟，疼死了")]
    [InlineData("唉声叹气")]
    [InlineData("叹气")]
    [InlineData("真的好难受")]
    [InlineData("想哭")]
    [InlineData("香菇，太难了")]
    public void TryAnalyze_EmotionalDistressAnchor_ReturnsSympathy(string text) {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = CreateContext(text);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Sympathy);
        } else {
            Assert.Fail($"Expected TryAnalyze to return true for: {text}");
        }
    }

    [Theory]
    [InlineData("长期失眠")]
    [InlineData("一直头疼")]
    [InlineData("胃痛得厉害")]
    [InlineData("经常头晕")]
    [InlineData("食欲不振")]
    public void TryAnalyze_PhysicalSymptomAnchor_ReturnsSympathy(string text) {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = CreateContext(text);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Sympathy);
        } else {
            Assert.Fail($"Expected TryAnalyze to return true for: {text}");
        }
    }

    [Theory]
    [InlineData("家人生病了")]
    [InlineData("在医院陪护")]
    [InlineData("送急诊了")]
    [InlineData("照顾病人")]
    public void TryAnalyze_FamilyDifficultyAnchor_ReturnsSympathy(string text) {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = CreateContext(text);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Sympathy);
        } else {
            Assert.Fail($"Expected TryAnalyze to return true for: {text}");
        }
    }

    [Theory]
    [InlineData("医药费太贵了")]
    [InlineData("负担不起")]
    [InlineData("看不起病")]
    [InlineData("经济困难")]
    public void TryAnalyze_EconomicPressureAnchor_ReturnsSympathy(string text) {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = CreateContext(text);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Sympathy);
        } else {
            Assert.Fail($"Expected TryAnalyze to return true for: {text}");
        }
    }

    [Theory]
    [InlineData("又通宵了")]
    [InlineData("连续熬夜好几天")]
    [InlineData("深夜加班")]
    [InlineData("没时间睡觉")]
    public void TryAnalyze_OverworkAnchor_ReturnsSympathy(string text) {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = CreateContext(text);

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Emotion.Should().Be(AestheticEmotion.Sympathy);
        } else {
            Assert.Fail($"Expected TryAnalyze to return true for: {text}");
        }
    }

    [Fact]
    public void TryAnalyze_NormalText_ReturnsFalse() {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = CreateContext("var x = 42;");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_NullResult_ReturnsFalse() {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = new ToolExecutionContext {
            ToolName = "read",
            Arguments = []
        };

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_EmptyText_ReturnsFalse() {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = CreateContext("");

        analyzer.TryAnalyze(context, out _).Should().BeFalse();
    }

    [Fact]
    public void TryAnalyze_FeedbackIncludesAnchorWord() {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = CreateContext("妈妈患上了癌症");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Message.Should().Contain("困境信号");
            feedback.Message.Should().Contain("端到端测试");
            feedback.Message.Should().Contain("标记字符串");
            feedback.Message.Should().Contain("验收依据");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for cancer keyword");
        }
    }

    [Fact]
    public void TryAnalyze_AlwaysProvidesSuggestion() {
        var analyzer = new SympathyAnalyzer(new CooldownService());
        var context = CreateContext("确诊癌症");

        if (analyzer.TryAnalyze(context, out var feedback)) {
            feedback.Suggestion.Should().NotBeNullOrEmpty();
            feedback.Suggestion.Should().Contain("端到端测试");
        } else {
            Assert.Fail("Expected TryAnalyze to return true for cancer keyword");
        }
    }
}
