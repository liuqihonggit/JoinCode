namespace Core.Tests.Security;

public sealed class LlmAutoModeClassifierTests {
    [Fact]
    public async Task Classify_RuleBlock_IsNeverOverriddenByLlm() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.Dangerous,
            Confidence = 0.5,
            Reason = "硬规则命中",
            Action = SecurityAction.Block
        });
        var judge = new Mock<IPermissionJudgeClient>();
        judge.Setup(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"classification":"safe","confidence":0.99,"reason":"AI 认为安全","action":"autoApprove"}""");
        await using var classifier = new LlmAutoModeClassifier(rule, judge.Object);

        var result = await classifier.ClassifyAsync(CreateRequest("echo a && rm -rf /"));

        Assert.Equal(SecurityAction.Block, result.Action);
        judge.Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "规则 Block 时不得调用 AI 改判");
    }

    [Fact]
    public async Task Classify_HighConfidence_ReturnsRuleResultDirectly() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.Safe,
            Confidence = 0.95,
            Reason = "只读操作",
            Action = SecurityAction.AutoApprove
        });
        await using var classifier = new LlmAutoModeClassifier(rule, judgeClient: null);

        var result = await classifier.ClassifyAsync(CreateRequest("read"));

        Assert.Equal(SecurityClassification.Safe, result.Classification);
        Assert.Equal(0.95, result.Confidence);
    }

    [Fact]
    public async Task Classify_LowConfidence_TriggersLlm() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.6,
            Reason = "未知操作",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockJudge("""{"classification":"lowRisk","confidence":0.9,"reason":"LLM分析安全","action":"autoApprove"}""");
        await using var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("test"));

        Assert.Equal(SecurityClassification.LowRisk, result.Classification);
        Assert.Equal(0.9, result.Confidence);
    }

    [Fact]
    public async Task Classify_ComplexCommand_TriggersLlm() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.LowRisk,
            Confidence = 0.85,
            Reason = "写入操作",
            Action = SecurityAction.AutoApprove
        });
        var llm = CreateMockJudge("""{"classification":"highRisk","confidence":0.88,"reason":"复杂管道链","action":"requireApproval"}""");
        await using var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("echo hello | grep foo && rm bar"));

        Assert.Equal(SecurityClassification.HighRisk, result.Classification);
        Assert.Equal(SecurityAction.RequireApproval, result.Action);
    }

    [Fact]
    public async Task Classify_NullJudgeClient_FallsBackToRule() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        await using var classifier = new LlmAutoModeClassifier(rule, judgeClient: null);

        var result = await classifier.ClassifyAsync(CreateRequest("test"));

        Assert.Equal(SecurityClassification.MediumRisk, result.Classification);
    }

    [Fact]
    public async Task Classify_SameCommandTwice_UsesCachedLlmResult() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var judge = new Mock<IPermissionJudgeClient>();
        judge.Setup(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"classification":"lowRisk","confidence":0.9,"reason":"cached","action":"autoApprove"}""");
        await using var classifier = new LlmAutoModeClassifier(rule, judge.Object);

        await classifier.ClassifyAsync(CreateRequest("same-command"));
        await classifier.ClassifyAsync(CreateRequest("same-command"));

        judge.Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "同一工具+命令在缓存 TTL 内不得重复调用 LLM");
    }

    [Fact]
    public async Task Classify_LlmError_FallsBackToRule() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockJudge(throwException: true);
        await using var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("test"));

        Assert.Equal(SecurityClassification.MediumRisk, result.Classification);
    }

    [Fact]
    public async Task Classify_InvalidLlmJson_FallsBackToRule() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockJudge("not valid json at all");
        await using var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("test"));

        Assert.Equal(SecurityClassification.MediumRisk, result.Classification);
    }

    [Fact]
    public async Task Classify_LlmReturnsDangerous_ParsesCorrectly() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockJudge("""{"classification":"dangerous","confidence":0.99,"reason":"rm -rf detected","action":"block"}""");
        await using var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("rm -rf /tmp && echo done"));

        Assert.Equal(SecurityClassification.Dangerous, result.Classification);
        Assert.Equal(SecurityAction.Block, result.Action);
        Assert.Equal(0.99, result.Confidence);
    }

    [Fact]
    public async Task Classify_LlmResponseWithExtraText_ExtractsJson() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockJudge("Here is the analysis:\n{\"classification\":\"highRisk\",\"confidence\":0.9,\"reason\":\"dangerous\",\"action\":\"requireApproval\"}\nDone.");
        await using var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("test"));

        Assert.Equal(SecurityClassification.HighRisk, result.Classification);
    }

    [Fact]
    public async Task Classify_LlmHighRiskAutoApprove_IsEscalatedToApproval() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "未知",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockJudge("""{"classification":"highRisk","confidence":0.99,"reason":"高风险但模型给出 autoApprove","action":"autoApprove"}""");
        await using var classifier = new LlmAutoModeClassifier(rule, llm);

        var result = await classifier.ClassifyAsync(CreateRequest("some high risk command"));

        Assert.Equal(SecurityClassification.HighRisk, result.Classification);
        Assert.Equal(SecurityAction.RequireApproval, result.Action);
    }

    private static IPermissionJudgeClient CreateMockJudge(string? response = null, bool throwException = false) {
        var mock = new Mock<IPermissionJudgeClient>();
        if (throwException) {
            mock.Setup(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("LLM error"));
        } else {
            mock.Setup(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response ?? string.Empty);
        }
        return mock.Object;
    }

    private static ClassificationRequest CreateRequest(string command) {
        return new ClassificationRequest {
            ToolName = "bash",
            Parameters = new Dictionary<string, JsonElement> { ["command"] = JsonSerializer.SerializeToElement(command, HooksJsonContext.Default.String) },
            OperationType = OperationType.Execute
        };
    }
}

internal sealed class StubClassifier : IAutoModeClassifier {
    private readonly ClassificationResult _result;
    public StubClassifier(ClassificationResult result) => _result = result;
    public Task<ClassificationResult> ClassifyAsync(ClassificationRequest request, CancellationToken ct = default) => Task.FromResult(_result);
}
