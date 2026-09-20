namespace Abs.Tests.Decision;

/// <summary>
/// TypedDecisionKind 枚举 + ITypedDecision 抽象层契约测试
/// 验证 [EnumValue] 源码生成器正确生成 ToValue/FromValue 映射
/// </summary>
public class TypedDecisionKindTests {
    #region [EnumValue] 字符串映射

    [Fact]
    public void ToValue_ReturnsCorrectStrings() {
        TypedDecisionKind.Noul.ToValue().Should().Be("noul");
        TypedDecisionKind.Choice.ToValue().Should().Be("choice");
        TypedDecisionKind.Score.ToValue().Should().Be("score");
    }

    [Fact]
    public void FromValue_ReturnsCorrectEnum() {
        TypedDecisionKindExtensions.FromValue("noul").Should().Be(TypedDecisionKind.Noul);
        TypedDecisionKindExtensions.FromValue("choice").Should().Be(TypedDecisionKind.Choice);
        TypedDecisionKindExtensions.FromValue("score").Should().Be(TypedDecisionKind.Score);
    }

    [Fact]
    public void FromValue_UnknownString_ReturnsNull() {
        TypedDecisionKindExtensions.FromValue("unknown").Should().BeNull();
    }

    [Fact]
    public void EnumConstants_HaveCorrectValues() {
        TypedDecisionKindEnumConstants.Noul.Should().Be("noul");
        TypedDecisionKindEnumConstants.Choice.Should().Be("choice");
        TypedDecisionKindEnumConstants.Score.Should().Be("score");
    }

    #endregion

    #region TypedDecisionQuestion 默认值

    [Fact]
    public void TypedDecisionQuestion_Default_HasEmptyInstructions() {
        var question = new TypedDecisionQuestion();

        question.Kind.Should().Be(TypedDecisionKind.Noul);
        question.Instructions.Should().BeEmpty();
        question.Options.Should().BeEmpty();
    }

    [Fact]
    public void TypedDecisionQuestion_WithChoiceKind_CanSetOptions() {
        var question = new TypedDecisionQuestion {
            Kind = TypedDecisionKind.Choice,
            Instructions = "Classify the sentiment",
            Options = ["positive", "negative", "neutral"]
        };

        question.Kind.Should().Be(TypedDecisionKind.Choice);
        question.Instructions.Should().Be("Classify the sentiment");
        question.Options.Should().HaveCount(3);
        question.Options.Should().Contain("positive");
    }

    #endregion

    #region TypedDecisionResult 默认值

    [Fact]
    public void TypedDecisionResult_Default_HasEmptyAnswers() {
        var result = new TypedDecisionResult();

        result.Answers.Should().BeEmpty();
        result.ModelId.Should().BeNull();
        result.Usage.Should().BeNull();
    }

    [Fact]
    public void TypedDecisionResult_WithAnswers_CanRetrieveDecision() {
        var decision = new TestDecision {
            QuestionName = "sentiment",
            Kind = TypedDecisionKind.Choice,
            Confidence = 0.95,
            RawValue = JsonDocument.Parse("\"positive\"").RootElement.Clone()
        };
        var result = new TypedDecisionResult {
            Answers = new Dictionary<string, ITypedDecision> { ["sentiment"] = decision },
            ModelId = "jev-latest",
            Usage = new TypedDecisionUsage { InputTokens = 42 }
        };

        result.Answers.Should().HaveCount(1);
        result.Answers["sentiment"].Should().BeSameAs(decision);
        result.ModelId.Should().Be("jev-latest");
        result.Usage!.InputTokens.Should().Be(42);
    }

    #endregion

    #region TypedDecisionUsage 默认值

    [Fact]
    public void TypedDecisionUsage_Default_HasZeroTokens() {
        var usage = new TypedDecisionUsage();

        usage.InputTokens.Should().Be(0);
    }

    #endregion

    /// <summary>测试用 ITypedDecision 桩实现</summary>
    private sealed class TestDecision : ITypedDecision {
        public string QuestionName { get; init; } = string.Empty;
        public TypedDecisionKind Kind { get; init; }
        public double Confidence { get; init; }
        public JsonElement RawValue { get; init; }
    }
}
