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
}
