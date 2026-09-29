namespace Llm.Tests.Adapters.LLM.QueryServices.Jev;

/// <summary>
/// JevQueryService 单元测试 — 构造/JevDecision/state 映射/降级流式
/// HTTP 集成测试留给 J7 E2E
/// </summary>
public class JevQueryServiceTests {
    private static JevQueryService CreateService() {
        var config = new ProviderConfig {
            Vendor = "jev",
            ApiKey = "jev-test-key",
            ModelId = "jev-latest",
            Definition = new FallbackProviderDefinition(ProtocolKind.Jev)
        };
        return new JevQueryService(config);
    }

    #region 构造

    [Fact]
    public void Constructor_WithValidConfig_ShouldCreateInstance() {
        var service = CreateService();
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ImplementsITypedDecisionService() {
        var service = CreateService();
        service.Should().BeAssignableTo<ITypedDecisionService>();
    }

    [Fact]
    public void Constructor_ImplementsIQueryService() {
        var service = CreateService();
        service.Should().BeAssignableTo<IQueryService>();
    }

    #endregion

    #region JevDecision

    [Fact]
    public void JevDecision_ImplementsITypedDecision() {
        var decision = new JevDecision {
            QuestionName = "test",
            Kind = TypedDecisionKind.Noul,
            Confidence = 0.95,
            RawValue = JsonElementHelper.FromDouble(0.95)
        };

        decision.Should().BeAssignableTo<ITypedDecision>();
        decision.QuestionName.Should().Be("test");
        decision.Kind.Should().Be(TypedDecisionKind.Noul);
        decision.Confidence.Should().Be(0.95);
        decision.RawValue.GetDouble().Should().Be(0.95);
    }

    [Fact]
    public void JevDecision_WithChoiceKind_HasStringRawValue() {
        var decision = new JevDecision {
            QuestionName = "sentiment",
            Kind = TypedDecisionKind.Choice,
            Confidence = 0.88,
            RawValue = JsonElementHelper.FromString("positive")
        };

        decision.RawValue.GetString().Should().Be("positive");
    }

    [Fact]
    public void JevDecision_WithScoreKind_HasDoubleRawValue() {
        var decision = new JevDecision {
            QuestionName = "rating",
            Kind = TypedDecisionKind.Score,
            Confidence = 0.92,
            RawValue = JsonElementHelper.FromDouble(7.5)
        };

        decision.RawValue.GetDouble().Should().Be(7.5);
    }

    #endregion

    #region 降级流式(需 HTTP mock,留给 J7 E2E)

    [Fact(Skip = "需 HTTP mock 或真实 API Key,留给 J7 E2E 验证")]
    public void GetStreamEventContentsAsync_ShouldYieldSingleEvent_WhenNonStreamFallback() {
        // J7 E2E:用真实 API Key 或 MockServer 验证降级流式
    }

    #endregion

    #region BuildStateFromMessageList — 4角色拼接

    [Theory]
    [InlineData(MessageRole.System, "[System]")]
    [InlineData(MessageRole.User, "[User]")]
    [InlineData(MessageRole.Assistant, "[Assistant]")]
    [InlineData(MessageRole.Tool, "[Tool]")]
    public void BuildStateFromMessageList_SingleRole_PrefixesContent(MessageRole role, string expectedPrefix) {
        var history = new MessageList { new(role, "body") };

        var state = JevQueryService.BuildStateFromMessageList(history);

        state.Should().StartWith(expectedPrefix);
        state.Should().Contain("body");
    }

    [Fact]
    public void BuildStateFromMessageList_MultipleMessages_ConcatenatesWithLineBreaks() {
        var history = new MessageList {
            new(MessageRole.System, "sys"),
            new(MessageRole.User, "ask"),
            new(MessageRole.Assistant, "ans"),
            new(MessageRole.Tool, "result")
        };

        var state = JevQueryService.BuildStateFromMessageList(history);

        state.Should().Contain("[System]: sys");
        state.Should().Contain("[User]: ask");
        state.Should().Contain("[Assistant]: ans");
        state.Should().Contain("[Tool]: result");
        state.Should().NotEndWith("\n", "TrimEnd 应去除尾部换行");
    }

    [Fact]
    public void BuildStateFromMessageList_EmptyHistory_ReturnsEmptyString() {
        var state = JevQueryService.BuildStateFromMessageList(new MessageList());
        state.Should().BeEmpty();
    }

    #endregion

    #region ConvertToDecision — Noul/Choice/Score/fallback 4分支

    [Fact]
    public void ConvertToDecision_NoulHasValue_ReturnsNoulKindWithDoubleRawValue() {
        var answer = new JevAnswer { Noul = 0.85, Confidence = 0.9 };

        var decision = JevQueryService.ConvertToDecision("q", answer);

        decision.Kind.Should().Be(TypedDecisionKind.Noul);
        decision.Confidence.Should().Be(0.9);
        decision.RawValue.GetDouble().Should().Be(0.85);
        decision.QuestionName.Should().Be("q");
    }

    [Fact]
    public void ConvertToDecision_ChoiceHasValue_ReturnsChoiceKindWithStringRawValue() {
        var answer = new JevAnswer { Choice = "positive", Confidence = 0.88 };

        var decision = JevQueryService.ConvertToDecision("sentiment", answer);

        decision.Kind.Should().Be(TypedDecisionKind.Choice);
        decision.RawValue.GetString().Should().Be("positive");
        decision.Confidence.Should().Be(0.88);
    }

    [Fact]
    public void ConvertToDecision_ScoreHasValue_ReturnsScoreKindWithDoubleRawValue() {
        var answer = new JevAnswer { Score = 7.5, Confidence = 0.92 };

        var decision = JevQueryService.ConvertToDecision("rating", answer);

        decision.Kind.Should().Be(TypedDecisionKind.Score);
        decision.RawValue.GetDouble().Should().Be(7.5);
    }

    [Fact]
    public void ConvertToDecision_AllNull_ReturnsFallbackNoulWithZeroConfidence() {
        var answer = new JevAnswer();

        var decision = JevQueryService.ConvertToDecision("unknown", answer);

        decision.Kind.Should().Be(TypedDecisionKind.Noul, "全 null 时 fallback 到 Noul");
        decision.Confidence.Should().Be(0);
        decision.RawValue.GetDouble().Should().Be(0.0);
    }

    [Fact]
    public void ConvertToDecision_ConfidenceNull_DefaultsToZero() {
        var answer = new JevAnswer { Noul = 0.5 };

        var decision = JevQueryService.ConvertToDecision("q", answer);

        decision.Confidence.Should().Be(0, "Confidence null 时 ?? 0");
    }

    [Fact]
    public void ConvertToDecision_NoulTakesPrecedenceOverChoiceAndScore() {
        var answer = new JevAnswer { Noul = 0.1, Choice = "x", Score = 2.0 };

        var decision = JevQueryService.ConvertToDecision("q", answer);

        decision.Kind.Should().Be(TypedDecisionKind.Noul, "Noul 优先判定");
    }

    [Fact]
    public void ConvertToDecision_ChoiceTakesPrecedenceOverScore() {
        var answer = new JevAnswer { Choice = "x", Score = 2.0 };

        var decision = JevQueryService.ConvertToDecision("q", answer);

        decision.Kind.Should().Be(TypedDecisionKind.Choice, "无 Noul 时 Choice 优先于 Score");
    }

    #endregion

    #region ConvertToTypedDecisionResult — 结果映射

    [Fact]
    public void ConvertToTypedDecisionResult_MapsAnswersAndModelId() {
        var response = new JevResponse {
            Model = "jev-model",
            Answers = new Dictionary<string, JevAnswer> {
                ["q1"] = new() { Noul = 0.7, Confidence = 0.9 }
            }
        };

        var result = JevQueryService.ConvertToTypedDecisionResult(response);

        result.ModelId.Should().Be("jev-model");
        result.Answers.Should().ContainKey("q1");
        result.Answers["q1"].Kind.Should().Be(TypedDecisionKind.Noul);
    }

    [Fact]
    public void ConvertToTypedDecisionResult_WithUsage_MapsInputTokens() {
        var response = new JevResponse {
            Model = "m",
            Answers = new Dictionary<string, JevAnswer>(),
            Usage = new JevUsage { InputTokens = 42 }
        };

        var result = JevQueryService.ConvertToTypedDecisionResult(response);

        result.Usage.Should().NotBeNull();
        result.Usage!.InputTokens.Should().Be(42);
    }

    [Fact]
    public void ConvertToTypedDecisionResult_UsageNull_ReturnsNullUsage() {
        var response = new JevResponse { Model = "m", Answers = new() };

        var result = JevQueryService.ConvertToTypedDecisionResult(response);

        result.Usage.Should().BeNull();
    }

    [Fact]
    public void ConvertToTypedDecisionResult_MultipleAnswers_AllMapped() {
        var response = new JevResponse {
            Model = "m",
            Answers = new Dictionary<string, JevAnswer> {
                ["noul_q"] = new() { Noul = 0.5 },
                ["choice_q"] = new() { Choice = "a" },
                ["score_q"] = new() { Score = 3.0 }
            }
        };

        var result = JevQueryService.ConvertToTypedDecisionResult(response);

        result.Answers.Should().HaveCount(3);
        result.Answers["noul_q"].Kind.Should().Be(TypedDecisionKind.Noul);
        result.Answers["choice_q"].Kind.Should().Be(TypedDecisionKind.Choice);
        result.Answers["score_q"].Kind.Should().Be(TypedDecisionKind.Score);
    }

    #endregion

    #region ConvertToApiMessage — 摘要构建

    [Fact]
    public void ConvertToApiMessage_NoulDecision_ContainsNoulInContent() {
        var result = new TypedDecisionResult {
            Answers = new Dictionary<string, ITypedDecision> {
                ["q"] = new JevDecision {
                    QuestionName = "q",
                    Kind = TypedDecisionKind.Noul,
                    Confidence = 0.95,
                    RawValue = JsonElementHelper.FromDouble(0.85)
                }
            },
            ModelId = "m"
        };

        var msg = JevQueryService.ConvertToApiMessage(result);

        msg.Role.Should().Be(MessageRole.Assistant);
        msg.Content.Should().Contain("noul=0.85");
        msg.Content.Should().Contain("confidence=0.95");
        msg.ModelId.Should().Be("m");
    }

    [Fact]
    public void ConvertToApiMessage_ChoiceDecision_ContainsChoiceInContent() {
        var result = new TypedDecisionResult {
            Answers = new Dictionary<string, ITypedDecision> {
                ["q"] = new JevDecision {
                    QuestionName = "q",
                    Kind = TypedDecisionKind.Choice,
                    Confidence = 0.8,
                    RawValue = JsonElementHelper.FromString("positive")
                }
            }
        };

        var msg = JevQueryService.ConvertToApiMessage(result);

        msg.Content.Should().Contain("choice=positive");
    }

    [Fact]
    public void ConvertToApiMessage_ScoreDecision_ContainsScoreInContent() {
        var result = new TypedDecisionResult {
            Answers = new Dictionary<string, ITypedDecision> {
                ["q"] = new JevDecision {
                    QuestionName = "q",
                    Kind = TypedDecisionKind.Score,
                    Confidence = 0.7,
                    RawValue = JsonElementHelper.FromDouble(8.5)
                }
            }
        };

        var msg = JevQueryService.ConvertToApiMessage(result);

        msg.Content.Should().Contain("score=8.5");
    }

    [Fact]
    public void ConvertToApiMessage_WithModelIdAndUsage_PopulatesMetadata() {
        var result = new TypedDecisionResult {
            Answers = new Dictionary<string, ITypedDecision>(),
            ModelId = "jev-m",
            Usage = new TypedDecisionUsage { InputTokens = 100 }
        };

        var msg = JevQueryService.ConvertToApiMessage(result);

        msg.Metadata.Should().ContainKey("JevModelId");
        msg.Metadata!["JevModelId"].GetString().Should().Be("jev-m");
        msg.Metadata.Should().ContainKey("JevInputTokens");
        msg.Metadata!["JevInputTokens"].GetInt32().Should().Be(100);
    }

    [Fact]
    public void ConvertToApiMessage_NullModelIdAndUsage_MetadataContainsNulls() {
        var result = new TypedDecisionResult {
            Answers = new Dictionary<string, ITypedDecision>(),
            ModelId = null,
            Usage = null
        };

        var msg = JevQueryService.ConvertToApiMessage(result);

        msg.Metadata!["JevModelId"].ValueKind.Should().Be(JsonValueKind.Null);
        msg.Metadata!["JevInputTokens"].ValueKind.Should().Be(JsonValueKind.Null);
    }

    #endregion
}
