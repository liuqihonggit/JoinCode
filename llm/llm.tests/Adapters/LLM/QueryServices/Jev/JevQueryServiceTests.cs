// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
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

    #region 降级流式 — HTTP mock 确定性测试

    [Fact]
    public async Task GetStreamEventContentsAsync_ShouldYieldSingleEvent_WhenNonStreamFallback() {
        // Jev 不支持流式,降级为非流式包装单次 yield — 用 HttpMessageHandler mock 消除 IO
        var responseJson = """{"model":"jev-latest","answers":{"default":{"choice":"positive","confidence":0.88}}}""";
        var handler = new FakeJevHttpHandler(new HttpResponseMessage {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var service = CreateServiceWithHandler(handler);

        var history = new MessageList {
            new(MessageRole.User, "Classify sentiment")
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in service.GetStreamEventContentsAsync(history)) {
            events.Add(evt);
        }

        events.Should().ContainSingle("Jev 不支持流式,降级为单次 yield");
        events[0].Role.Should().Be(MessageRole.Assistant);
        events[0].Content.Should().Contain("choice=positive");
        events[0].Content.Should().Contain("confidence=0.88");
        events[0].ModelId.Should().Be("jev-latest");
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_PreservesMetadata_FromApiMessage() {
        // 验证降级包装时 StreamEvent 携带 ApiMessage 的 metadata
        var responseJson = """{"model":"jev-latest","answers":{"default":{"noul":0.7,"confidence":0.9}},"usage":{"input_tokens":10}}""";
        var handler = new FakeJevHttpHandler(new HttpResponseMessage {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var service = CreateServiceWithHandler(handler);

        var events = new List<StreamEvent>();
        await foreach (var evt in service.GetStreamEventContentsAsync(new MessageList { new(MessageRole.User, "q") })) {
            events.Add(evt);
        }

        events.Should().ContainSingle();
        events[0].Metadata.Should().ContainKey("JevModelId");
        events[0].Metadata!["JevModelId"].GetString().Should().Be("jev-latest");
        events[0].Metadata.Should().ContainKey("JevInputTokens");
        events[0].Metadata!["JevInputTokens"].GetInt32().Should().Be(10);
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

    #region GetTypedDecisionsAsync — HTTP mock 确定性测试

    [Fact]
    public async Task GetTypedDecisionsAsync_ShouldDeserializeResponse_AndMapToTypedDecisionResult() {
        // Arrange — 伪造 Jev 响应 JSON(Noul 决策 + usage)
        var responseJson = """{"id":"resp_1","model":"jev-latest","answers":{"q1":{"noul":0.9,"confidence":0.95}},"usage":{"input_tokens":42}}""";
        var handler = new FakeJevHttpHandler(new HttpResponseMessage {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var service = CreateServiceWithHandler(handler);

        var questions = new Dictionary<string, TypedDecisionQuestion> {
            ["q1"] = new() { Kind = TypedDecisionKind.Noul, Instructions = "Is it true?" }
        };

        // Act
        var result = await service.GetTypedDecisionsAsync("test state", questions);

        // Assert — 反序列化 + ConvertToTypedDecisionResult 映射正确
        result.ModelId.Should().Be("jev-latest");
        result.Answers.Should().ContainKey("q1");
        result.Answers["q1"].Kind.Should().Be(TypedDecisionKind.Noul);
        result.Answers["q1"].Confidence.Should().Be(0.95);
        result.Answers["q1"].RawValue.GetDouble().Should().Be(0.9);
        result.Usage.Should().NotBeNull();
        result.Usage!.InputTokens.Should().Be(42);

        // Assert — 请求体包含正确的 model + questions
        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().Contain("\"model\":\"jev-latest\"");
        handler.LastRequestBody.Should().Contain("\"q1\"");
        handler.LastRequestBody.Should().Contain("\"type\":\"noul\"");
    }

    [Fact]
    public async Task GetTypedDecisionsAsync_WithChoiceAnswer_MapsChoiceKind() {
        var responseJson = """{"model":"jev-model","answers":{"sentiment":{"choice":"positive","confidence":0.88}}}""";
        var handler = new FakeJevHttpHandler(new HttpResponseMessage {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var service = CreateServiceWithHandler(handler);

        var questions = new Dictionary<string, TypedDecisionQuestion> {
            ["sentiment"] = new() {
                Kind = TypedDecisionKind.Choice,
                Instructions = "Classify",
                Options = ["positive", "negative"]
            }
        };

        var result = await service.GetTypedDecisionsAsync("feedback", questions);

        result.Answers["sentiment"].Kind.Should().Be(TypedDecisionKind.Choice);
        result.Answers["sentiment"].RawValue.GetString().Should().Be("positive");
        result.Answers["sentiment"].Confidence.Should().Be(0.88);

        // 请求体应包含 options(Choice 类型)
        handler.LastRequestBody.Should().Contain("\"options\":[\"positive\",\"negative\"]");
    }

    [Fact]
    public async Task GetTypedDecisionsAsync_WithMultipleAnswers_MapsAll() {
        var responseJson = """{"model":"m","answers":{"q1":{"noul":0.8},"q2":{"choice":"yes","confidence":0.7},"q3":{"score":5.0}}}""";
        var handler = new FakeJevHttpHandler(new HttpResponseMessage {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var service = CreateServiceWithHandler(handler);

        var questions = new Dictionary<string, TypedDecisionQuestion> {
            ["q1"] = new() { Kind = TypedDecisionKind.Noul, Instructions = "a" },
            ["q2"] = new() { Kind = TypedDecisionKind.Choice, Instructions = "b" },
            ["q3"] = new() { Kind = TypedDecisionKind.Score, Instructions = "c" }
        };

        var result = await service.GetTypedDecisionsAsync("state", questions);

        result.Answers.Should().HaveCount(3);
        result.Answers["q1"].Kind.Should().Be(TypedDecisionKind.Noul);
        result.Answers["q2"].Kind.Should().Be(TypedDecisionKind.Choice);
        result.Answers["q3"].Kind.Should().Be(TypedDecisionKind.Score);
    }

    [Fact]
    public async Task GetTypedDecisionsAsync_ShouldThrow_WhenHttpReturnsError() {
        var handler = new FakeJevHttpHandler(new HttpResponseMessage {
            StatusCode = HttpStatusCode.InternalServerError,
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var service = CreateServiceWithHandler(handler);

        var act = () => service.GetTypedDecisionsAsync("state", new Dictionary<string, TypedDecisionQuestion>());

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetTypedDecisionsAsync_ShouldThrow_WhenResponseJsonIsNull() {
        // 返回 "null" JSON → 反序列化为 null → 抛 InvalidOperationException
        var handler = new FakeJevHttpHandler(new HttpResponseMessage {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        });
        var service = CreateServiceWithHandler(handler);

        var act = () => service.GetTypedDecisionsAsync("state", new Dictionary<string, TypedDecisionQuestion>());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region GetApiMessageContentsAsync — HTTP mock 确定性测试

    [Fact]
    public async Task GetApiMessageContentsAsync_ShouldBuildStateAndReturnSummaryMessage() {
        // 验证:BuildStateFromMessageList 拼接 → GetTypedDecisionsAsync → ConvertToApiMessage 摘要
        var responseJson = """{"model":"jev-latest","answers":{"default":{"noul":0.8,"confidence":0.9}}}""";
        var handler = new FakeJevHttpHandler(new HttpResponseMessage {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var service = CreateServiceWithHandler(handler);

        var history = new MessageList {
            new(MessageRole.System, "You are a classifier"),
            new(MessageRole.User, "Is this spam?")
        };

        var messages = await service.GetApiMessageContentsAsync(history);

        // 返回单条摘要 ApiMessage
        messages.Should().ContainSingle();
        var msg = messages[0];
        msg.Role.Should().Be(MessageRole.Assistant);
        msg.Content.Should().Contain("default");
        msg.Content.Should().Contain("noul=0.8");
        msg.Content.Should().Contain("confidence=0.90");
        msg.ModelId.Should().Be("jev-latest");

        // 请求体应包含 BuildStateFromMessageList 拼接的 state(含 [System] 和 [User] 前缀)
        handler.LastRequestBody.Should().Contain("[System]");
        handler.LastRequestBody.Should().Contain("You are a classifier");
        handler.LastRequestBody.Should().Contain("[User]");
        handler.LastRequestBody.Should().Contain("Is this spam?");
    }

    [Fact]
    public async Task GetApiMessageContentsAsync_WithChoiceAnswer_SummaryContainsChoice() {
        var responseJson = """{"model":"m","answers":{"default":{"choice":"positive","confidence":0.77}}}""";
        var handler = new FakeJevHttpHandler(new HttpResponseMessage {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var service = CreateServiceWithHandler(handler);

        var messages = await service.GetApiMessageContentsAsync(new MessageList { new(MessageRole.User, "text") });

        messages.Should().ContainSingle();
        messages[0].Content.Should().Contain("choice=positive");
        messages[0].Content.Should().Contain("confidence=0.77");
    }

    [Fact]
    public async Task GetApiMessageContentsAsync_PopulatesMetadata_WithJevModelIdAndTokens() {
        var responseJson = """{"model":"jev-m","answers":{"default":{"noul":0.5}},"usage":{"input_tokens":99}}""";
        var handler = new FakeJevHttpHandler(new HttpResponseMessage {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var service = CreateServiceWithHandler(handler);

        var messages = await service.GetApiMessageContentsAsync(new MessageList { new(MessageRole.User, "x") });

        messages[0].Metadata.Should().ContainKey("JevModelId");
        messages[0].Metadata!["JevModelId"].GetString().Should().Be("jev-m");
        messages[0].Metadata.Should().ContainKey("JevInputTokens");
        messages[0].Metadata!["JevInputTokens"].GetInt32().Should().Be(99);
    }

    #endregion

    #region HTTP mock 辅助

    private static JevQueryService CreateServiceWithHandler(FakeJevHttpHandler handler) {
        var config = new ProviderConfig {
            Vendor = "jev",
            ApiKey = "jev-test-key",
            ModelId = "jev-latest",
            Definition = new FallbackProviderDefinition(ProtocolKind.Jev)
        };
        // 设置 BaseAddress — 端点为相对路径(chat/completions),需 BaseAddress 拼成绝对 URI
        // Handler 拦截所有请求,实际 URL 不影响测试结果
        var client = new HttpClient(handler) {
            BaseAddress = new Uri("https://api.typesafe.ai/v1/")
        };
        return new JevQueryService(config, client);
    }

    /// <summary>
    /// 伪造 Jev HTTP 响应的 HttpMessageHandler — 捕获请求体供断言,返回预设响应
    /// 确定性:无网络 IO,无时序,单次请求-响应
    /// </summary>
    private sealed class FakeJevHttpHandler : HttpMessageHandler {
        private readonly HttpResponseMessage _response;

        /// <summary>捕获最近一次请求体(JSON 字符串),供测试断言请求内容</summary>
        public string? LastRequestBody { get; private set; }

        public FakeJevHttpHandler(HttpResponseMessage response) {
            _response = response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            if (request.Content is not null) {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return _response;
        }
    }

    #endregion
}
