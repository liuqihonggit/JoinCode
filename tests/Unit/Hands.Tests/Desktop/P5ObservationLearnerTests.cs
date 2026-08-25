namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// P5 观察学习单元测试
/// </summary>
public sealed class P5ObservationLearnerTests
{
    private static DesktopOperation Op(DesktopOperationKind kind = DesktopOperationKind.Click, int x = 10, int y = 20, bool succeeded = true) =>
        new(kind, x, y, null, null, null, DateTimeOffset.UtcNow, succeeded, null);

    private static ObservedSession MakeSession(params DesktopOperation[] ops) =>
        new("test-session", ops, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static Mock<IQueryService> CreateQueryMock(string response)
    {
        var mock = new Mock<IQueryService>();
        mock.Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, response) });
        return mock;
    }

    #region Constructor

    [Fact]
    public void Constructor_NullQueryService_Throws()
    {
        var act = () => new ObservationLearner(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region BuildOperationsDescription

    [Fact]
    public void BuildOperationsDescription_EmptySession_ReturnsEmpty()
    {
        var session = MakeSession();
        var desc = ObservationLearner.BuildOperationsDescription(session);
        desc.Should().BeEmpty();
    }

    [Fact]
    public void BuildOperationsDescription_SingleClick_ContainsIndexAndKind()
    {
        var session = MakeSession(Op(DesktopOperationKind.Click, 100, 200));
        var desc = ObservationLearner.BuildOperationsDescription(session);
        desc.Should().Contain("[1]");
        desc.Should().Contain("Click");
        desc.Should().Contain("(100,200)");
        desc.Should().Contain("✓");
    }

    [Fact]
    public void BuildOperationsDescription_FailedOp_ShowsCrossMark()
    {
        var session = MakeSession(Op(DesktopOperationKind.Click, 0, 0, succeeded: false));
        var desc = ObservationLearner.BuildOperationsDescription(session);
        desc.Should().Contain("✗");
        desc.Should().NotContain("✓");
    }

    [Fact]
    public void BuildOperationsDescription_WithText_IncludesText()
    {
        var op = new DesktopOperation(DesktopOperationKind.TypeText, 0, 0, "hello", null, null, DateTimeOffset.UtcNow, true, null);
        var session = MakeSession(op);
        var desc = ObservationLearner.BuildOperationsDescription(session);
        desc.Should().Contain("text=\"hello\"");
    }

    [Fact]
    public void BuildOperationsDescription_WithMouseAction_IncludesAction()
    {
        var op = new DesktopOperation(DesktopOperationKind.Click, 5, 5, null, MouseAction.RightClick, null, DateTimeOffset.UtcNow, true, null);
        var session = MakeSession(op);
        var desc = ObservationLearner.BuildOperationsDescription(session);
        desc.Should().Contain("action=Right");
    }

    [Fact]
    public void BuildOperationsDescription_WithModifiers_IncludesMods()
    {
        var op = new DesktopOperation(DesktopOperationKind.KeyPress, 0, 0, null, null, KeyModifier.Control, DateTimeOffset.UtcNow, true, null);
        var session = MakeSession(op);
        var desc = ObservationLearner.BuildOperationsDescription(session);
        desc.Should().Contain("mods=Control");
    }

    [Fact]
    public void BuildOperationsDescription_MultipleOps_SequentialIndex()
    {
        var session = MakeSession(Op(), Op(), Op());
        var desc = ObservationLearner.BuildOperationsDescription(session);
        desc.Should().Contain("[1]");
        desc.Should().Contain("[2]");
        desc.Should().Contain("[3]");
    }

    #endregion

    #region ExtractJson

    [Fact]
    public void ExtractJson_NullOrEmpty_ReturnsEmpty()
    {
        ObservationLearner.ExtractJson("").Should().BeEmpty();
        ObservationLearner.ExtractJson("   ").Should().BeEmpty();
    }

    [Fact]
    public void ExtractJson_PlainJson_ReturnedAsIs()
    {
        var json = """{"name":"test"}""";
        ObservationLearner.ExtractJson(json).Should().Be(json);
    }

    [Fact]
    public void ExtractJson_JsonInMarkdownFence_Extracted()
    {
        var input = """
            ```json
            {"name":"test"}
            ```
            """;
        var result = ObservationLearner.ExtractJson(input);
        result.Should().Be("""{"name":"test"}""");
    }

    [Fact]
    public void ExtractJson_JsonWithSurroundingText_ExtractsBraces()
    {
        var input = "Here is the result: {\"name\":\"test\"} done.";
        var result = ObservationLearner.ExtractJson(input);
        result.Should().Be("""{"name":"test"}""");
    }

    [Fact]
    public void ExtractJson_NoBraces_ReturnsTrimmed()
    {
        ObservationLearner.ExtractJson("just text").Should().Be("just text");
    }

    #endregion

    #region ParseAbstractLogic

    [Fact]
    public void ParseAbstractLogic_ValidJson_ReturnsParsed()
    {
        var json = """{"name":"打开应用","pattern":"点击图标","parameters":"target={app}","steps":["定位","点击"],"confidence":0.9}""";
        var result = ObservationLearner.ParseAbstractLogic(json, "fallback");
        result.Name.Should().Be("打开应用");
        result.Pattern.Should().Be("点击图标");
        result.Parameters.Should().Be("target={app}");
        result.Steps.Should().Equal("定位", "点击");
        result.Confidence.Should().Be(0.9);
    }

    [Fact]
    public void ParseAbstractLogic_EmptyResponse_ReturnsFallback()
    {
        var result = ObservationLearner.ParseAbstractLogic("", "fallback");
        result.Name.Should().Be("fallback");
        result.Pattern.Should().Be("无法抽象");
        result.Confidence.Should().Be(0.0);
    }

    [Fact]
    public void ParseAbstractLogic_InvalidJson_ReturnsParseFailed()
    {
        var result = ObservationLearner.ParseAbstractLogic("not json at all {{{", "fallback");
        result.Name.Should().Be("fallback");
        result.Pattern.Should().Be("解析失败");
        result.Confidence.Should().Be(0.0);
    }

    [Fact]
    public void ParseAbstractLogic_MissingName_UsesFallback()
    {
        var json = """{"pattern":"test","steps":[],"confidence":0.5}""";
        var result = ObservationLearner.ParseAbstractLogic(json, "fallback");
        result.Name.Should().Be("fallback");
        result.Pattern.Should().Be("test");
    }

    [Fact]
    public void ParseAbstractLogic_MissingConfidence_DefaultsToHalf()
    {
        var json = """{"name":"test","pattern":"","parameters":"","steps":[]}""";
        var result = ObservationLearner.ParseAbstractLogic(json, "fallback");
        result.Confidence.Should().Be(0.5);
    }

    [Fact]
    public void ParseAbstractLogic_StepsNotArray_EmptySteps()
    {
        var json = """{"name":"test","pattern":"","parameters":"","steps":"not array","confidence":0.5}""";
        var result = ObservationLearner.ParseAbstractLogic(json, "fallback");
        result.Steps.Should().BeEmpty();
    }

    [Fact]
    public void ParseAbstractLogic_StepsWithEmptyStrings_Filtered()
    {
        var json = """{"name":"test","pattern":"","parameters":"","steps":["real","","  "],"confidence":0.5}""";
        var result = ObservationLearner.ParseAbstractLogic(json, "fallback");
        result.Steps.Should().Equal("real");
    }

    [Fact]
    public void ParseAbstractLogic_MarkdownFencedJson_Parsed()
    {
        var input = """
            ```json
            {"name":"fenced","pattern":"p","parameters":"","steps":["s1"],"confidence":0.8}
            ```
            """;
        var result = ObservationLearner.ParseAbstractLogic(input, "fallback");
        result.Name.Should().Be("fenced");
        result.Confidence.Should().Be(0.8);
    }

    #endregion

    #region AbstractAsync

    [Fact]
    public async Task AbstractAsync_ValidResponse_ReturnsParsedLogic()
    {
        var llmResponse = """{"name":"登录模式","pattern":"输入凭据并点击登录","parameters":"user={u},pass={p}","steps":["输入用户名","输入密码","点击登录"],"confidence":0.85}""";
        var mock = CreateQueryMock(llmResponse);
        var learner = new ObservationLearner(mock.Object);

        var session = MakeSession(
            new DesktopOperation(DesktopOperationKind.TypeText, 100, 200, "admin", null, null, DateTimeOffset.UtcNow, true, null),
            new DesktopOperation(DesktopOperationKind.TypeText, 100, 250, "pass", null, null, DateTimeOffset.UtcNow, true, null),
            Op(DesktopOperationKind.Click, 150, 300));

        var result = await learner.AbstractAsync(session);

        result.Name.Should().Be("登录模式");
        result.Steps.Should().HaveCount(3);
        result.Confidence.Should().Be(0.85);
    }

    [Fact]
    public async Task AbstractAsync_EmptyResponse_ReturnsFallback()
    {
        var mock = CreateQueryMock("");
        var learner = new ObservationLearner(mock.Object);

        var result = await learner.AbstractAsync(MakeSession(Op()));

        result.Name.Should().Be("test-session");
        result.Pattern.Should().Be("无法抽象");
    }

    [Fact]
    public async Task AbstractAsync_NullSession_Throws()
    {
        var mock = CreateQueryMock("");
        var learner = new ObservationLearner(mock.Object);

        var act = async () => await learner.AbstractAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AbstractAsync_CallsQueryServiceOnce()
    {
        var mock = CreateQueryMock("""{"name":"x","pattern":"","parameters":"","steps":[],"confidence":0.5}""");
        var learner = new ObservationLearner(mock.Object);

        await learner.AbstractAsync(MakeSession(Op()));

        mock.Verify(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region OptimizeAsync

    [Fact]
    public async Task OptimizeAsync_ValidResponse_ReturnsText()
    {
        var mock = CreateQueryMock("建议：合并步骤1和2");
        var learner = new ObservationLearner(mock.Object);

        var logic = new AbstractOperationLogic("test", "pattern", "p", ["s1", "s2"], 0.8);
        var result = await learner.OptimizeAsync(logic);

        result.Should().Be("建议：合并步骤1和2");
    }

    [Fact]
    public async Task OptimizeAsync_EmptyResponse_ReturnsDefaultMessage()
    {
        var mock = CreateQueryMock("");
        var learner = new ObservationLearner(mock.Object);

        var logic = new AbstractOperationLogic("test", "pattern", "p", [], 0.5);
        var result = await learner.OptimizeAsync(logic);

        result.Should().Be("无法生成优化建议");
    }

    [Fact]
    public async Task OptimizeAsync_NullLogic_Throws()
    {
        var mock = CreateQueryMock("");
        var learner = new ObservationLearner(mock.Object);

        var act = async () => await learner.OptimizeAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion
}
