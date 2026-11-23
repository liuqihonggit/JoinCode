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
    public async Task Classify_NonShellTool_LowConfidence_UsesPathSummaryWithoutContent() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.6,
            Reason = "未知写入路径",
            Action = SecurityAction.RequireConfirmation
        });
        string? capturedPrompt = null;
        var judge = new Mock<IPermissionJudgeClient>();
        judge.Setup(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, prompt, _) => capturedPrompt = prompt)
            .ReturnsAsync("""{"classification":"lowRisk","confidence":0.9,"reason":"普通工作区路径","action":"autoApprove"}""");
        await using var classifier = new LlmAutoModeClassifier(rule, judge.Object);
        var request = new ClassificationRequest {
            ToolName = FileToolNameEnumConstants.FileWrite,
            Parameters = new Dictionary<string, JsonElement> {
                ["file_path"] = JsonSerializer.SerializeToElement("/workspace/notes.md", HooksJsonContext.Default.String),
                ["content"] = JsonSerializer.SerializeToElement("secret file body", HooksJsonContext.Default.String),
            },
            OperationType = OperationType.Auto,
        };

        var result = await classifier.ClassifyAsync(request);

        Assert.Equal(SecurityClassification.LowRisk, result.Classification);
        Assert.Equal(SecurityAction.AutoApprove, result.Action);
        var prompt = capturedPrompt ?? throw new Xunit.Sdk.XunitException("期望 AI Judge 收到判定提示词");
        prompt.Should().Contain("file_path");
        prompt.Should().Contain("/workspace/notes.md");
        prompt.Should().NotContain("secret file body", "文件正文不得进入判定提示词");
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

    /// <summary>同一作用域下可复用不降低确认要求的判定；顶层参数顺序不影响键。</summary>
    [Fact]
    public async Task Classify_SameScopeAndArguments_UsesCachedLlmResult() {
        var judge = CreateMockJudge("""{"classification":"mediumRisk","confidence":0.9,"reason":"cached","action":"requireConfirmation"}""");
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(CreateGrayRuleResult()), judge);
        using var scope = CreateCacheScope().EnterScope();
        var first = CreateCacheRequest();
        var second = CreateCacheRequest();
        var command = second.Parameters["command"];
        var description = second.Parameters["description"];
        second.Parameters.Clear();
        second.Parameters.Add("description", description);
        second.Parameters.Add("command", command);

        await classifier.ClassifyAsync(first);
        (await classifier.ClassifyAsync(second)).Action.Should().Be(SecurityAction.RequireConfirmation);

        Mock.Get(judge).Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>读取命中不续期；达到绝对期限时清除旧候选，模型无响应则回落规则。</summary>
    [Fact]
    public async Task Classify_CacheHitDoesNotExtendAbsoluteExpiration() {
        var clock = new FakeTimeProvider();
        var ruleResult = CreateGrayRuleResult();
        var judge = new Mock<IPermissionJudgeClient>();
        judge.SetupSequence(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"classification":"mediumRisk","confidence":0.9,"reason":"cached","action":"requireConfirmation"}""")
            .ReturnsAsync((string?)null)
            .ReturnsAsync("""{"classification":"mediumRisk","confidence":0.9,"reason":"fresh","action":"requireConfirmation"}""");
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(ruleResult), judge.Object,
            fileSystem: null, auditPath: "", cacheTimeProvider: clock);
        using var scope = CreateCacheScope().EnterScope();
        var request = CreateCacheRequest();

        (await classifier.ClassifyAsync(request)).Reason.Should().Be("cached");
        clock.Advance(TimeSpan.FromMinutes(29));
        (await classifier.ClassifyAsync(request)).Reason.Should().Be("cached");
        judge.Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        clock.Advance(TimeSpan.FromMinutes(1));
        (await classifier.ClassifyAsync(request)).Should().BeSameAs(ruleResult);
        classifier.CachedCandidateCount.Should().Be(0);
        (await classifier.ClassifyAsync(request)).Reason.Should().Be("fresh");
        judge.Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    /// <summary>填满实际容量后，新请求仍走模型，缓存不因继续写入而突破上限。</summary>
    [Fact]
    public async Task Classify_CacheRemainsBoundedWhenFull() {
        const int capacity = 512;
        var judge = CreateMockJudge("""{"classification":"mediumRisk","confidence":0.9,"reason":"valid","action":"requireConfirmation"}""");
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(CreateGrayRuleResult()), judge,
            fileSystem: null, auditPath: "", cacheTimeProvider: new FakeTimeProvider());
        foreach (var index in Enumerable.Range(0, capacity)) {
            using var itemScope = CreateCacheScope($"candidate-{index}").EnterScope();
            await classifier.ClassifyAsync(CreateCacheRequest(description: $"candidate-{index}"));
        }
        classifier.CachedCandidateCount.Should().Be(capacity);

        using var overflowScope = CreateCacheScope("candidate-overflow").EnterScope();
        (await classifier.ClassifyAsync(CreateCacheRequest(description: "overflow"))).Reason.Should().Be("valid");
        classifier.CachedCandidateCount.Should().BeLessThanOrEqualTo(capacity);
        Mock.Get(judge).Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(capacity + 1));
    }

    /// <summary>同一操作摘要不可跨会话、代理、目录、模式或实际参数复用。</summary>
    [Theory]
    [InlineData("session")]
    [InlineData("agent")]
    [InlineData("cwd")]
    [InlineData("mode")]
    [InlineData("arguments")]
    public async Task Classify_CacheContextChanges_RechecksJudge(string dimension) {
        var judge = CreateMockJudge("""{"classification":"mediumRisk","confidence":0.9,"reason":"scoped","action":"requireConfirmation"}""");
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(CreateGrayRuleResult()), judge);
        var agent = CreateCacheScope();
        using var scope = agent.EnterScope();
        await classifier.ClassifyAsync(CreateCacheRequest());

        var nextAgent = dimension == "agent" ? CreateCacheScope(agentId: "other-agent") : agent;
        if (dimension == "session") nextAgent.SessionId = "other-session";
        if (dimension == "cwd") nextAgent.CwdOverride = Path.Combine(Path.GetTempPath(), "other-worktree");
        using var nextScope = nextAgent.EnterScope();
        var request = CreateCacheRequest(mode: dimension == "mode" ? PermissionMode.Unattended : PermissionMode.Auto,
            description: dimension == "arguments" ? "different description" : "probe");
        await classifier.ClassifyAsync(request);

        Mock.Get(judge).Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>Bash 的显式绝对目录优先于任务默认目录，默认目录变化不应让同一个显式调用失去命中。</summary>
    [Fact]
    public async Task Classify_ExplicitDirectory_TakesPrecedenceOverAgentDirectory() {
        var judge = CreateMockJudge("""{"classification":"mediumRisk","confidence":0.9,"reason":"explicit directory","action":"requireConfirmation"}""");
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(CreateGrayRuleResult()), judge);
        var agent = CreateCacheScope();
        using var scope = agent.EnterScope();
        var request = CreateCacheRequest();
        var explicitDirectory = Path.Combine(Path.GetTempPath(), "explicit-judge-directory");
        request.Parameters["working_directory"] = JsonSerializer.SerializeToElement(explicitDirectory, HooksJsonContext.Default.String);

        await classifier.ClassifyAsync(request);
        agent.CwdOverride = "relative-worktree";
        await classifier.ClassifyAsync(request);

        Mock.Get(judge).Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>规则每次重新判断；即使已有候选，后续硬拒绝也不能进入模型或被缓存替代。</summary>
    [Fact]
    public async Task Classify_NewHardBlock_WinsOverWarmCache() {
        var judge = CreateMockJudge("""{"classification":"mediumRisk","confidence":0.9,"reason":"cached","action":"requireConfirmation"}""");
        var block = new ClassificationResult {
            Classification = SecurityClassification.Dangerous,
            Confidence = 0.95,
            Reason = "规则更新后拒绝",
            Action = SecurityAction.Block,
        };
        var classifierRules = new Mock<IAutoModeClassifier>();
        classifierRules.SetupSequence(x => x.ClassifyAsync(It.IsAny<ClassificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateGrayRuleResult())
            .ReturnsAsync(block);
        await using var classifier = new LlmAutoModeClassifier(classifierRules.Object, judge);
        using var scope = CreateCacheScope().EnterScope();
        var request = CreateCacheRequest();

        (await classifier.ClassifyAsync(request)).Reason.Should().Be("cached");
        (await classifier.ClassifyAsync(request)).Should().BeSameAs(block);

        Mock.Get(judge).Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>未知上下文或超过指纹限额的参数不入缓存，正常走实时判定。</summary>
    [Theory]
    [InlineData("missing-session")]
    [InlineData("relative-dir")]
    [InlineData("no-mode")]
    [InlineData("large-argument")]
    [InlineData("non-bash")]
    public async Task Classify_CacheIneligible_StillRunsJudge(string condition) {
        var judge = CreateMockJudge("""{"classification":"mediumRisk","confidence":0.9,"reason":"fresh","action":"requireConfirmation"}""");
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(CreateGrayRuleResult()), judge);
        var agent = CreateCacheScope();
        if (condition == "missing-session") agent.SessionId = null;
        if (condition == "relative-dir") agent.CwdOverride = "relative-worktree";
        using var scope = agent.EnterScope();
        var request = CreateCacheRequest(
            mode: condition == "no-mode" ? null : PermissionMode.Auto,
            description: condition == "large-argument" ? new string('x', 32 * 1024) : "probe",
            toolName: condition == "non-bash" ? ShellToolNameEnumConstants.Powershell : ShellToolNameEnumConstants.Bash);

        await classifier.ClassifyAsync(request);
        (await classifier.ClassifyAsync(request)).Reason.Should().Be("fresh");

        Mock.Get(judge).Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>正式判定的缓存不得重复放宽确认；模型不可用时按本次规则结果处理。</summary>
    [Fact]
    public async Task Classify_CachedApprovalCannotReplaceFreshLiveDecision() {
        var judge = new Mock<IPermissionJudgeClient>();
        judge.SetupSequence(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"classification":"lowRisk","confidence":0.9,"reason":"initial","action":"autoApprove"}""")
            .ReturnsAsync((string?)null);
        var ruleResult = CreateGrayRuleResult();
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(ruleResult), judge.Object);
        using var scope = CreateCacheScope().EnterScope();
        var request = CreateCacheRequest();

        (await classifier.ClassifyAsync(request)).Action.Should().Be(SecurityAction.AutoApprove);
        (await classifier.ClassifyAsync(request)).Should().BeSameAs(ruleResult);

        judge.Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>影子判定不得经由缓存改变后续正式判定，即使影子候选更严格。</summary>
    [Fact]
    public async Task Classify_ShadowCandidateCannotAffectLaterLiveDecision() {
        var judge = new Mock<IPermissionJudgeClient>();
        judge.SetupSequence(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"classification":"highRisk","confidence":0.9,"reason":"shadow","action":"requireApproval"}""")
            .ReturnsAsync((string?)null);
        var ruleResult = CreateGrayRuleResult();
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(ruleResult), judge.Object);
        using var scope = CreateCacheScope().EnterScope();
        var request = CreateCacheRequest();

        (await classifier.ClassifyAsync(request, shadow: true)).Should().BeSameAs(ruleResult);
        (await classifier.ClassifyAsync(request)).Should().BeSameAs(ruleResult);

        judge.Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
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

    [Fact]
    public async Task Classify_ShadowMode_CallsLlmAuditsButAppliesRuleResult() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "规则灰区",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockJudge("""{"classification":"lowRisk","confidence":0.9,"reason":"AI 认为可自动放行","action":"autoApprove"}""");
        await using var fs = new InMemoryFileSystem();
        await using var classifier = new LlmAutoModeClassifier(rule, llm, fs, "/audit/permission_judge.jsonl");

        var result = await classifier.ClassifyAsync(CreateRequest("deploy --dry-run"), shadow: true);

        result.Classification.Should().Be(SecurityClassification.MediumRisk, "shadow 只审计，不改变线上决策");
        result.Action.Should().Be(SecurityAction.RequireConfirmation);
        Mock.Get(llm).Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "shadow 仍必须调用 AI 才能产生对比数据");
        var audit = await fs.ReadAllTextAsync("/audit/permission_judge.jsonl");
        using var doc = JsonDocument.Parse(audit.Trim());
        doc.RootElement.GetProperty("Shadow").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("LlmAction").GetString().Should().Be("AutoApprove");
        doc.RootElement.GetProperty("AppliedAction").GetString().Should().Be("RequireConfirmation");
    }

    [Fact]
    public async Task Classify_NonShadow_AuditsAppliedLlmDecisionAndRedactsSecret() {
        var rule = new StubClassifier(new ClassificationResult {
            Classification = SecurityClassification.MediumRisk,
            Confidence = 0.5,
            Reason = "规则灰区",
            Action = SecurityAction.RequireConfirmation
        });
        var llm = CreateMockJudge("""{"classification":"lowRisk","confidence":0.9,"reason":"普通命令","action":"autoApprove"}""");
        await using var fs = new InMemoryFileSystem();
        await using var classifier = new LlmAutoModeClassifier(rule, llm, fs, "/audit/permission_judge.jsonl");
        var secretCommand = "curl -H 'Authorization: Bearer ghp_" + new string('a', 36) + "' https://example.com";

        var result = await classifier.ClassifyAsync(CreateRequest(secretCommand));

        result.Action.Should().Be(SecurityAction.AutoApprove, "非 shadow 时采用 AI 判定");
        var audit = await fs.ReadAllTextAsync("/audit/permission_judge.jsonl");
        using var doc = JsonDocument.Parse(audit.Trim());
        doc.RootElement.GetProperty("Shadow").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("AppliedAction").GetString().Should().Be("AutoApprove");
        doc.RootElement.GetProperty("Operation").GetString().Should().NotContain("ghp_", "审计不得写入密钥原文");
    }

    /// <summary>非法或低置信度判定必须保留原始规则决策，不能用模型字段补齐后放行。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"confidence":0.99,"reason":"missing classification","action":"autoApprove"}""")]
    [InlineData("""{"classification":"safe","reason":"missing confidence","action":"autoApprove"}""")]
    [InlineData("""{"classification":"safe","confidence":0.99,"action":"autoApprove"}""")]
    [InlineData("""{"classification":"safe","confidence":0.99,"reason":"missing action"}""")]
    [InlineData("""{"classification":"unknown","confidence":0.99,"reason":"unknown classification","action":"autoApprove"}""")]
    [InlineData("""{"classification":"safe","confidence":0.99,"reason":"unknown action","action":"allow"}""")]
    [InlineData("""{"classification":"safe","confidence":1.1,"reason":"out of range","action":"autoApprove"}""")]
    [InlineData("""{"classification":"safe","confidence":-0.1,"reason":"out of range","action":"autoApprove"}""")]
    [InlineData("""{"classification":"safe","confidence":"0.99","reason":"wrong type","action":"autoApprove"}""")]
    [InlineData("""{"classification":"safe","confidence":0.99,"reason":"duplicate action","action":"block","action":"autoApprove"}""")]
    [InlineData("""{"classification":"safe","confidence":0.79,"reason":"below threshold","action":"autoApprove"}""")]
    [InlineData("""{"classification":"dangerous","confidence":0.79,"reason":"uncertain block","action":"block"}""")]
    public async Task Classify_UnusableJudgeResponse_PreservesRuleResult(string response) {
        var ruleResult = CreateGrayRuleResult();
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(ruleResult), CreateMockJudge(response));

        var result = await classifier.ClassifyAsync(CreateRequest("probe"));

        result.Should().BeSameAs(ruleResult);
    }

    /// <summary>置信度等于配置门槛时，合法判定可以生效。</summary>
    [Fact]
    public async Task Classify_ConfidenceAtThreshold_AppliesValidResult() {
        var judge = CreateMockJudge("""{"classification":"LOWRISK","confidence":0.85,"reason":"valid","action":"AUTOAPPROVE"}""");
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(CreateGrayRuleResult()), judge, llmThreshold: 0.85);

        var result = await classifier.ClassifyAsync(CreateRequest("probe"));

        result.Action.Should().Be(SecurityAction.AutoApprove);
        result.Confidence.Should().Be(0.85);
    }

    /// <summary>失败不入缓存；同一个调用可获得后续有效判定，缓存命中也必须审计当次的 shadow 状态。</summary>
    [Fact]
    public async Task Classify_FallbackIsNotCached_AndCacheHitAuditsCurrentDecision() {
        var auditPath = Path.Combine(Path.GetTempPath(), "audit", "permission_judge.jsonl");
        var ruleResult = CreateGrayRuleResult();
        var judge = new Mock<IPermissionJudgeClient>();
        judge.SetupSequence(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"classification":"safe","reason":"invalid response marker","action":"autoApprove"}""")
            .ReturnsAsync("""{"classification":"lowRisk","confidence":0.7,"reason":"uncertain","action":"autoApprove"}""")
            .ReturnsAsync("""{"classification":"lowRisk","confidence":0.95,"reason":"valid","action":"autoApprove"}""");
        await using var fs = new InMemoryFileSystem();
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(ruleResult), judge.Object, fs, auditPath);
        using var scope = CreateCacheScope().EnterScope();
        var request = CreateCacheRequest();

        (await classifier.ClassifyAsync(request)).Should().BeSameAs(ruleResult);
        (await classifier.ClassifyAsync(request)).Should().BeSameAs(ruleResult);
        (await classifier.ClassifyAsync(request, shadow: true)).Should().BeSameAs(ruleResult);
        (await classifier.ClassifyAsync(request, shadow: true)).Should().BeSameAs(ruleResult);

        judge.Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        var audit = await fs.ReadAllTextAsync(auditPath);
        audit.Should().NotContain("invalid response marker");
        var lines = audit.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(4);
        var invalid = ReadAuditRecord(lines[0]);
        invalid.FallbackReason.Should().Be("invalid-response");
        invalid.LlmAction.Should().BeNull();
        invalid.AppliedAction.Should().Be("RequireConfirmation");
        var uncertain = ReadAuditRecord(lines[1]);
        uncertain.FallbackReason.Should().Be("low-confidence");
        uncertain.LlmConfidence.Should().Be(0.7);
        uncertain.AppliedAction.Should().Be("RequireConfirmation");
        var fresh = ReadAuditRecord(lines[2]);
        fresh.CacheHit.Should().BeFalse();
        fresh.FallbackReason.Should().BeNull();
        fresh.Shadow.Should().BeTrue();
        fresh.LlmAction.Should().Be("AutoApprove");
        fresh.AppliedAction.Should().Be("RequireConfirmation");
        var cached = ReadAuditRecord(lines[3]);
        cached.CacheHit.Should().BeTrue();
        cached.Shadow.Should().BeTrue();
        cached.LlmAction.Should().Be("AutoApprove");
        cached.AppliedAction.Should().Be("RequireConfirmation");
    }

    /// <summary>调用方取消必须传播，不能被当成模型不可用后继续做权限决策。</summary>
    [Fact]
    public async Task Classify_CallerCancellation_Propagates() {
        using var cts = new CancellationTokenSource();
        var judge = new Mock<IPermissionJudgeClient>();
        judge.Setup(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, CancellationToken ct) => {
                cts.Cancel();
                return Task.FromCanceled<string?>(ct);
            });
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(CreateGrayRuleResult()), judge.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync(CreateRequest("probe"), cts.Token));
    }

    /// <summary>预算耗尽后不再调用模型，直接回落规则并写预算审计。</summary>
    [Fact]
    public async Task Classify_BudgetExhausted_FallsBackToRuleAndAudits() {
        var rule = CreateGrayRuleResult();
        var judge = CreateMockJudge("""{"classification":"lowRisk","confidence":0.95,"reason":"valid","action":"autoApprove"}""");
        await using var fs = new InMemoryFileSystem();
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(rule), judge, fs, "/audit/permission_judge.jsonl");
        using var scope = CreateCacheScope("budget-agent").EnterScope();

        (await classifier.ClassifyAsync(CreateBudgetRequest("probe-1", maxCalls: 2))).Action.Should().Be(SecurityAction.AutoApprove);
        (await classifier.ClassifyAsync(CreateBudgetRequest("probe-2", maxCalls: 2))).Action.Should().Be(SecurityAction.AutoApprove);
        var exhausted = await classifier.ClassifyAsync(CreateBudgetRequest("probe-3", maxCalls: 2));

        exhausted.Should().BeSameAs(rule, "预算耗尽必须回落规则结果，不得默认放行");
        Mock.Get(judge).Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        var lines = (await fs.ReadAllTextAsync("/audit/permission_judge.jsonl")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3);
        var first = ReadAuditRecord(lines[0]);
        first.PolicyVersion.Should().Be("judge-2026-11-24.1");
        first.ContextHash.Should().HaveLength(64);
        first.BudgetRemaining.Should().Be(1);
        first.BudgetExhausted.Should().BeFalse();
        ReadAuditRecord(lines[1]).BudgetRemaining.Should().Be(0);
        var limited = ReadAuditRecord(lines[2]);
        limited.FallbackReason.Should().Be("budget-exhausted");
        limited.BudgetExhausted.Should().BeTrue();
        limited.BudgetRemaining.Should().Be(0);
        limited.LlmAction.Should().BeNull();
    }

    /// <summary>预算只作用于当前代理任务；新任务作用域重新获得完整额度。</summary>
    [Fact]
    public async Task Classify_BudgetIsScopedToTaskContext() {
        var rule = CreateGrayRuleResult();
        var judge = CreateMockJudge("""{"classification":"lowRisk","confidence":0.95,"reason":"valid","action":"autoApprove"}""");
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(rule), judge);

        using (var firstTask = CreateCacheScope("budget-task-a").EnterScope()) {
            (await classifier.ClassifyAsync(CreateBudgetRequest("probe-a1", maxCalls: 1))).Action.Should().Be(SecurityAction.AutoApprove);
            (await classifier.ClassifyAsync(CreateBudgetRequest("probe-a2", maxCalls: 1))).Should().BeSameAs(rule);
        }
        using (var secondTask = CreateCacheScope("budget-task-b").EnterScope()) {
            (await classifier.ClassifyAsync(CreateBudgetRequest("probe-b1", maxCalls: 1))).Action.Should().Be(SecurityAction.AutoApprove);
        }

        Mock.Get(judge).Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>缓存命中不消耗调用预算；只有真实模型调用才计数。</summary>
    [Fact]
    public async Task Classify_CacheHit_DoesNotConsumeBudget() {
        var rule = CreateGrayRuleResult();
        var judge = CreateMockJudge("""{"classification":"mediumRisk","confidence":0.9,"reason":"valid","action":"requireConfirmation"}""");
        await using var fs = new InMemoryFileSystem();
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(rule), judge, fs, "/audit/permission_judge.jsonl");
        using var scope = CreateCacheScope("cache-budget-agent").EnterScope();
        var cachedRequest = CreateCacheRequest(description: "cache-budget", maxCalls: 1);

        (await classifier.ClassifyAsync(cachedRequest)).Reason.Should().Be("valid");
        (await classifier.ClassifyAsync(cachedRequest)).Reason.Should().Be("valid");
        (await classifier.ClassifyAsync(CreateCacheRequest(description: "cache-budget-miss", maxCalls: 1))).Should().BeSameAs(rule);

        Mock.Get(judge).Verify(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        var lines = (await fs.ReadAllTextAsync("/audit/permission_judge.jsonl")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3);
        var cached = ReadAuditRecord(lines[1]);
        cached.CacheHit.Should().BeTrue();
        cached.BudgetRemaining.Should().Be(0, "缓存命中不消耗预算");
        var limited = ReadAuditRecord(lines[2]);
        limited.BudgetExhausted.Should().BeTrue();
        limited.FallbackReason.Should().Be("budget-exhausted");
    }

    /// <summary>判定超时必须回落 no-response，不能无限阻塞权限检查。</summary>
    [Fact]
    public async Task Classify_JudgeTimeout_FallsBackNoResponse() {
        var rule = CreateGrayRuleResult();
        var judge = new Mock<IPermissionJudgeClient>();
        judge.Setup(x => x.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, CancellationToken token) => {
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
                return """{"classification":"lowRisk","confidence":0.95,"reason":"slow","action":"autoApprove"}""";
            });
        await using var fs = new InMemoryFileSystem();
        await using var classifier = new LlmAutoModeClassifier(new StubClassifier(rule), judge.Object, fs, "/audit/permission_judge.jsonl");

        var result = await classifier.ClassifyAsync(CreateBudgetRequest("slow-probe", timeoutMs: 100));

        result.Should().BeSameAs(rule);
        var record = ReadAuditRecord((await fs.ReadAllTextAsync("/audit/permission_judge.jsonl")).Trim());
        record.FallbackReason.Should().Be("no-response");
        record.BudgetExhausted.Should().BeFalse();
    }

    /// <summary>建立带会话、代理及绝对工作目录的缓存测试作用域。</summary>
    private static SubAgentContext CreateCacheScope(string agentId = "cache-agent") => new() {
        AgentId = agentId,
        Role = JoinCode.Abstractions.Models.Agent.AgentRole.Executor,
        Task = "Judge cache test",
        SessionId = "cache-session",
        CwdOverride = Path.Combine(Path.GetTempPath(), "judge-cache-worktree"),
    };

    /// <summary>建立带调用预算与超时的判定请求。</summary>
    private static ClassificationRequest CreateBudgetRequest(string command, int maxCalls = 10, int? timeoutMs = null) => new() {
        ToolName = ShellToolNameEnumConstants.Bash,
        OperationType = OperationType.Execute,
        JudgeMaxCallsPerTask = maxCalls,
        JudgeTimeoutMs = timeoutMs,
        Parameters = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement(command, HooksJsonContext.Default.String),
        },
    };

    /// <summary>建立命令相同、可独立改变模式及非摘要参数的缓存请求。</summary>
    private static ClassificationRequest CreateCacheRequest(
        PermissionMode? mode = PermissionMode.Auto,
        string description = "probe",
        string toolName = ShellToolNameEnumConstants.Bash,
        int? maxCalls = null,
        int? timeoutMs = null) => new() {
        ToolName = toolName,
        OperationType = OperationType.Execute,
        CacheMode = mode,
        JudgeMaxCallsPerTask = maxCalls,
        JudgeTimeoutMs = timeoutMs,
        Parameters = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement("probe", HooksJsonContext.Default.String),
            ["description"] = JsonSerializer.SerializeToElement(description, HooksJsonContext.Default.String),
        },
    };

    /// <summary>使用生产 JSON 类型元数据读取一条审计记录。</summary>
    private static PermissionJudgeAuditRecord ReadAuditRecord(string line) =>
        JsonSerializer.Deserialize(line, LlmSecurityJsonContext.Default.PermissionJudgeAuditRecord)
        ?? throw new Xunit.Sdk.XunitException("期望读到 AI 判定审计记录");

    /// <summary>构造需要用户确认的灰区规则结果，用于验证保守回落。</summary>
    private static ClassificationResult CreateGrayRuleResult() => new() {
        Classification = SecurityClassification.MediumRisk,
        Confidence = 0.5,
        Reason = "规则灰区",
        Action = SecurityAction.RequireConfirmation,
    };

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
