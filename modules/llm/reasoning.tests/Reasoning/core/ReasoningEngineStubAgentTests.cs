namespace JoinCode.Reasoning.Tests;

/// <summary>
/// 推理引擎桩子 Agent 测试 — 用 StubReasoningAgent 消除 LLM/IO 依赖，断言对抗流程调用顺序与 URL 验证降级
/// </summary>
public sealed class ReasoningEngineStubAgentTests {
    [Fact]
    public async Task RunAdversarialProcessAsync_WithStubAgents_InvokesAllAgentsOnce() {
        await using var prosecutor = new StubReasoningAgent(AgentRole.Prosecutor);
        await using var defender = new StubReasoningAgent(AgentRole.Defender);
        await using var judge = new StubReasoningAgent(AgentRole.Judge);
        var engine = CreateEngine(prosecutor, defender, judge);

        await engine.RunAdversarialProcessAsync(CancellationToken.None);

        Assert.Equal(1, prosecutor.ReasonCallCount);
        Assert.Equal(1, defender.ReasonCallCount);
        Assert.Equal(1, judge.ReasonCallCount);
    }

    [Fact]
    public async Task RunAdversarialProcessAsync_WithStubAgents_AllAgentsReceiveContextWithDag() {
        await using var prosecutor = new StubReasoningAgent(AgentRole.Prosecutor);
        await using var defender = new StubReasoningAgent(AgentRole.Defender);
        await using var judge = new StubReasoningAgent(AgentRole.Judge);
        var engine = CreateEngine(prosecutor, defender, judge);

        await engine.RunAdversarialProcessAsync(CancellationToken.None);

        Assert.NotNull(prosecutor.ReceivedContexts[0].Dag);
        Assert.NotNull(defender.ReceivedContexts[0].Dag);
        Assert.NotNull(judge.ReceivedContexts[0].Dag);
        Assert.NotNull(prosecutor.ReceivedContexts[0].ConeOrchestrator);
    }

    [Fact]
    public async Task AddAssumptionsAsync_WithStubAgentReturningEvidence_AppliesEvidenceToDag() {
        var claimId = "claim-stub";
        var evidenceAction = new AgentAction {
            AgentRole = AgentRole.Prosecutor,
            AffectedClaimIds = { claimId },
            Evidence = {
                new EvidenceRecord {
                    Id = "ev-stub",
                    Content = "桩子证据",
                    Category = EvidenceCategory.Documentary,
                    TrustLevel = TrustLevel.Moderate,
                    SubmittedBy = AgentRole.Prosecutor,
                },
            },
        };
        await using var prosecutor = new StubReasoningAgent(AgentRole.Prosecutor, evidenceAction);
        await using var defender = new StubReasoningAgent(AgentRole.Defender);
        await using var judge = new StubReasoningAgent(AgentRole.Judge);
        var engine = CreateEngine(prosecutor, defender, judge);

        var item = new DataItem { Id = claimId, Content = "假定", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        // 桩子证据经 ApplyAgentAction 添加到 DAG，建立 SUPPORTS 边
        Assert.Contains("ev-stub", engine.Dag.Nodes.Keys);
        Assert.Single(engine.Dag.Edges);
        Assert.Equal("SUPPORTS", engine.Dag.Edges.Values.First().Label);
    }

    [Fact]
    public async Task AddAssumptionsAsync_RegistersConeFragmentsForAllRoles() {
        var engine = CreateEngine();
        var item = new DataItem { Id = "claim-cone", Content = "假定", State = DataState.Assumption };

        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        foreach (var role in new[] { AgentRole.Prosecutor, AgentRole.Defender, AgentRole.Judge }) {
            var cone = engine.ConeOrchestrator.GetRole(role);
            Assert.NotNull(cone);
            Assert.True(cone.AllFragments.Count > 0, $"{role} 视锥应注册片段");
            Assert.Contains(cone.AllFragments.Values, f => f.SourceItemId == item.Id);
        }
    }

    [Fact]
    public async Task VerifyAllEvidenceLinksAsync_WithInvalidUrl_DowngradesEvidenceTrustLevel() {
        var engine = CreateEngine();
        var item = new DataItem { Id = "claim-url", Content = "假定", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);
        var evidence = new EvidenceRecord {
            Id = "ev-url",
            Content = "带URL证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.DirectEvidence,
            SubmittedBy = AgentRole.Prosecutor,
            SourceUrl = "https://example.com/doc",
        };
        engine.AddEvidence(evidence, item.Id);

        var handler = new StubHttpHandler(HttpStatusCode.NotFound);
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, new HttpClient(handler));
        engine.SetUrlVerifier(verifier);

        await engine.VerifyAllEvidenceLinksAsync();

        var node = engine.Dag.Nodes["ev-url"];
        Assert.Equal(TrustLevel.Unreliable, node.Payload.TrustLevel);
    }

    [Fact]
    public async Task VerifyAllEvidenceLinksAsync_WithValidUrl_PreservesEvidenceTrustLevel() {
        var engine = CreateEngine();
        var item = new DataItem { Id = "claim-ok", Content = "假定", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);
        var evidence = new EvidenceRecord {
            Id = "ev-ok",
            Content = "带URL证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.DirectEvidence,
            SubmittedBy = AgentRole.Prosecutor,
            SourceUrl = "https://example.com/doc",
        };
        engine.AddEvidence(evidence, item.Id);

        var handler = new StubHttpHandler(HttpStatusCode.OK);
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, new HttpClient(handler));
        engine.SetUrlVerifier(verifier);

        await engine.VerifyAllEvidenceLinksAsync();

        var node = engine.Dag.Nodes["ev-ok"];
        Assert.Equal(TrustLevel.DirectEvidence, node.Payload.TrustLevel);
    }

    [Fact]
    public async Task VerifyAllEvidenceLinksAsync_WhenNoUrlVerifier_IsNoOp() {
        var engine = CreateEngine();
        var item = new DataItem { Id = "claim-noop", Content = "假定", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);
        var evidence = new EvidenceRecord {
            Id = "ev-noop",
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.DirectEvidence,
            SubmittedBy = AgentRole.Prosecutor,
            SourceUrl = "https://example.com/doc",
        };
        engine.AddEvidence(evidence, item.Id);

        await engine.VerifyAllEvidenceLinksAsync();

        var node = engine.Dag.Nodes["ev-noop"];
        Assert.Equal(TrustLevel.DirectEvidence, node.Payload.TrustLevel);
    }

    private static ReasoningEngine CreateEngine(
        StubReasoningAgent? prosecutor = null,
        StubReasoningAgent? defender = null,
        StubReasoningAgent? judge = null) {
        var agents = new ReasoningAgent[] {
            prosecutor ?? new StubReasoningAgent(AgentRole.Prosecutor),
            defender ?? new StubReasoningAgent(AgentRole.Defender),
            judge ?? new StubReasoningAgent(AgentRole.Judge),
        };
        return new ReasoningEngine(
            agents,
            NullLogger<ReasoningEngine>.Instance,
            new ReasoningOptions { MaxAdversarialRounds = 10, MaxTokens = 100000 });
    }

    private sealed class StubHttpHandler : HttpMessageHandler {
        private readonly HttpStatusCode _statusCode;

        public StubHttpHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(_statusCode) {
                Content = new StringContent("body"),
            });
    }
}
