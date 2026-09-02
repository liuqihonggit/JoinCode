namespace Core.Tests.Security;

public sealed class LlmAutoModeClassifierTests
{
    [Fact]
    public async Task Classify_HighConfidence_ReturnsRuleResultDirectly()
    {
        var rule = new StubClassifier(new ClassificationResult
        {
            Classification = SecurityClassification.Safe,
            Confidence = 0.95,
            Reason = "只读操作",
            Action = SecurityAction.AutoApprove
        });
        var classifier = new LlmAutoModeClassifier(rule, queryEngine: null);

        var result = await classifier.ClassifyAsync(CreateRequest("read"));

        Assert.Equal(SecurityClassification.Safe, result.Classification);
        Assert.Equal(0.95, result.Confidence);
    }

    [Fact]
    public async Task Classify_LowConfidence_TriggersLlm()
    {
        var rule = new StubClassifier(new ClassificationResult
        {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.6,
            Reason = "未知操作",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockEngine("""{"classification":"lowRisk","confidence":0.9,"reason":"LLM分析安全","action":"autoApprove"}""");
        var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("test"));

        Assert.Equal(SecurityClassification.LowRisk, result.Classification);
        Assert.Equal(0.9, result.Confidence);
    }

    [Fact]
    public async Task Classify_ComplexCommand_TriggersLlm()
    {
        var rule = new StubClassifier(new ClassificationResult
        {
            Classification = SecurityClassification.LowRisk,
            Confidence = 0.85,
            Reason = "写入操作",
            Action = SecurityAction.AutoApprove
        });
        var llm = CreateMockEngine("""{"classification":"highRisk","confidence":0.88,"reason":"复杂管道链","action":"requireApproval"}""");
        var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("echo hello | grep foo && rm bar"));

        Assert.Equal(SecurityClassification.HighRisk, result.Classification);
        Assert.Equal(SecurityAction.RequireApproval, result.Action);
    }

    [Fact]
    public async Task Classify_NullQueryEngine_FallsBackToRule()
    {
        var rule = new StubClassifier(new ClassificationResult
        {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var classifier = new LlmAutoModeClassifier(rule, queryEngine: null);

        var result = await classifier.ClassifyAsync(CreateRequest("test"));

        Assert.Equal(SecurityClassification.MediumRisk, result.Classification);
    }

    [Fact]
    public async Task Classify_LlmError_FallsBackToRule()
    {
        var rule = new StubClassifier(new ClassificationResult
        {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockEngine(throwException: true);
        var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("test"));

        Assert.Equal(SecurityClassification.MediumRisk, result.Classification);
    }

    [Fact]
    public async Task Classify_InvalidLlmJson_FallsBackToRule()
    {
        var rule = new StubClassifier(new ClassificationResult
        {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockEngine("not valid json at all");
        var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("test"));

        Assert.Equal(SecurityClassification.MediumRisk, result.Classification);
    }

    [Fact]
    public async Task Classify_LlmReturnsDangerous_ParsesCorrectly()
    {
        var rule = new StubClassifier(new ClassificationResult
        {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockEngine("""{"classification":"dangerous","confidence":0.99,"reason":"rm -rf detected","action":"block"}""");
        var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("rm -rf /tmp && echo done"));

        Assert.Equal(SecurityClassification.Dangerous, result.Classification);
        Assert.Equal(SecurityAction.Block, result.Action);
        Assert.Equal(0.99, result.Confidence);
    }

    [Fact]
    public async Task Classify_LlmResponseWithExtraText_ExtractsJson()
    {
        var rule = new StubClassifier(new ClassificationResult
        {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockEngine("Here is the analysis:\n{\"classification\":\"highRisk\",\"confidence\":0.9,\"reason\":\"dangerous\",\"action\":\"requireApproval\"}\nDone.");
        var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("test"));

        Assert.Equal(SecurityClassification.HighRisk, result.Classification);
    }

    private static IQueryEngine CreateMockEngine(string? response = null, bool throwException = false)
    {
        var mock = new Mock<IQueryEngine>();
        if (throwException)
        {
            mock.Setup(x => x.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("LLM error"));
        }
        else
        {
            mock.Setup(x => x.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response ?? string.Empty);
        }
        return mock.Object;
    }

    private static ClassificationRequest CreateRequest(string command)
    {
        return new ClassificationRequest
        {
            ToolName = "Bash",
            Parameters = new Dictionary<string, JsonElement> { ["command"] = JsonSerializer.SerializeToElement(command) },
            OperationType = OperationType.Execute
        };
    }
}

internal sealed class StubClassifier : IAutoModeClassifier
{
    private readonly ClassificationResult _result;
    public StubClassifier(ClassificationResult result) => _result = result;
    public Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default) => Task.FromResult(_result);
}
