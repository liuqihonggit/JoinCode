namespace JoinCode.Reasoning.Tests.Engine;

public sealed class ReasoningEngineEdgeCaseTests
{
    [Fact]
    public async Task AddAssumptionsAsync_ShouldRejectDuplicateContentEvenWithDifferentIds()
    {
        var engine = CreateEngine();
        var item1 = new DataItem { Id = "id1", Content = "重复", State = DataState.Assumption };
        var item2 = new DataItem { Id = "id2", Content = "重复", State = DataState.Assumption };

        await engine.AddAssumptionsAsync([item1], CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.AddAssumptionsAsync([item2], CancellationToken.None));
    }

    [Fact]
    public async Task AddAssumptionsAsync_AllowsRejectedDuplicateContent()
    {
        var engine = CreateEngine();
        var item = new DataItem { Id = "id1", Content = "内容", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        engine.Dag.Nodes[item.Id].Payload.State = DataState.Rejected;

        var item2 = new DataItem { Id = "id2", Content = "内容", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item2], CancellationToken.None);

        Assert.Equal(2, engine.GetAllItems().Count());
    }

    [Fact]
    public async Task AddEvidence_WhenClaimDoesNotExist_LogsWarningAndReturns()
    {
        var engine = CreateEngine();
        var evidence = new EvidenceRecord
        {
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };

        engine.AddEvidence(evidence, "missing-claim");

        Assert.Empty(engine.GetAllEvidence());
    }

    [Fact]
    public async Task AddEvidence_WouldCreateCycle_RemovesNodeAndReturns()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var evidence = new EvidenceRecord
        {
            Id = item.Id,
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };

        engine.AddEvidence(evidence, item.Id);

        Assert.Empty(engine.GetAllEvidence());
    }

    [Fact]
    public async Task PropagateEvidenceFailure_WhenEvidenceNotFound_ReturnsWithoutError()
    {
        var engine = CreateEngine();

        engine.PropagateEvidenceFailure("missing");

        Assert.True(true);
    }

    [Fact]
    public async Task PropagateEvidenceFailure_DowngradesEvidenceTrustLevel()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var evidence = new EvidenceRecord
        {
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.DirectEvidence,
            SubmittedBy = AgentRole.Prosecutor,
        };
        engine.AddEvidence(evidence, item.Id);

        engine.PropagateEvidenceFailure(evidence.Id);

        var node = engine.Dag.Nodes[evidence.Id];
        Assert.Equal(TrustLevel.Unreliable, node.Payload.TrustLevel);
        Assert.Equal(DataState.Rejected, node.Payload.State);
    }

    [Fact]
    public async Task ApplyVerdicts_Accept_ShouldCreateFactNodeAndVerdictEdge()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var verdict = new Verdict
        {
            ClaimId = item.Id,
            Decision = VerdictDecision.Accept,
            Reason = "证据充分",
            Confidence = 90,
        };

        await RunApplyVerdicts(engine, [verdict]);

        var claimNode = engine.Dag.Nodes[item.Id];
        Assert.Equal(DataState.Fact, claimNode.Payload.State);
        Assert.Equal(90, claimNode.Payload.Confidence);
        Assert.Contains(engine.Dag.Edges, e => e.Value.Label == "DECIDES");
    }

    [Fact]
    public async Task ApplyVerdicts_Reject_ShouldSetRejectedState()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var verdict = new Verdict
        {
            ClaimId = item.Id,
            Decision = VerdictDecision.Reject,
            Reason = "证据不足",
            Confidence = 10,
        };

        await RunApplyVerdicts(engine, [verdict]);

        var node = engine.Dag.Nodes[item.Id];
        Assert.Equal(DataState.Rejected, node.Payload.State);
        Assert.Equal(10, node.Payload.Confidence);
    }

    [Fact]
    public async Task ApplyVerdicts_Pending_ShouldSetPendingEvidenceState()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var verdict = new Verdict
        {
            ClaimId = item.Id,
            Decision = VerdictDecision.Pending,
            Reason = "需要补充",
            Confidence = 50,
        };

        await RunApplyVerdicts(engine, [verdict]);

        var node = engine.Dag.Nodes[item.Id];
        Assert.Equal(DataState.PendingEvidence, node.Payload.State);
    }

    [Fact]
    public async Task ApplyVerdicts_PartiallyAccept_ShouldSetVerifiedState()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var verdict = new Verdict
        {
            ClaimId = item.Id,
            Decision = VerdictDecision.PartiallyAccept,
            Reason = "部分接受",
            Confidence = 70,
        };

        await RunApplyVerdicts(engine, [verdict]);

        var node = engine.Dag.Nodes[item.Id];
        Assert.Equal(DataState.Verified, node.Payload.State);
        Assert.Equal("法官部分接受", node.Payload.VerifiedBy);
    }

    [Fact]
    public async Task ApplyVerdicts_ClaimNotFound_DoesNothing()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var verdict = new Verdict
        {
            ClaimId = "missing",
            Decision = VerdictDecision.Accept,
            Reason = "证据充分",
            Confidence = 90,
        };

        await RunApplyVerdicts(engine, [verdict]);

        Assert.Empty(engine.GetFacts());
    }

    [Fact]
    public async Task Reset_ClearsAllNodesAndBudget()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        engine.Reset();

        Assert.Empty(engine.GetAllItems());
        var budget = engine.GetBudgetStatus();
        Assert.Equal(0, budget.RoundsUsed);
        Assert.Equal(0, budget.TokensUsed);
    }

    [Fact]
    public async Task RunAdversarialProcessAsync_WhenBudgetExhausted_DoesNotIncrementRound()
    {
        var options = new ReasoningOptions { MaxAdversarialRounds = 0, MaxTokens = 100000 };
        var engine = CreateEngine(options);

        await engine.RunAdversarialProcessAsync(CancellationToken.None);

        var budget = engine.GetBudgetStatus();
        Assert.Equal(0, budget.RoundsUsed);
    }

    [Fact]
    public async Task GetSummary_ShouldCountEvidenceNodes()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var evidence = new EvidenceRecord
        {
            Content = "证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };
        engine.AddEvidence(evidence, item.Id);

        var summary = engine.GetSummary();
        Assert.Equal(1, summary.TotalEvidence);
    }

    [Fact]
    public async Task SetUrlVerifier_AndRun_ShouldNotThrow()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var handler = new TestHandler();
        var httpClient = new HttpClient(handler);
        var verifier = new EvidenceUrlVerifier(NullLogger<EvidenceUrlVerifier>.Instance, httpClient);
        engine.SetUrlVerifier(verifier);

        await engine.RunAdversarialProcessAsync(CancellationToken.None);

        Assert.True(true);
    }

    [Fact]
    public async Task ContinueAsync_RoundsOnly_DoesNotRefillTokens()
    {
        var options = new ReasoningOptions { MaxAdversarialRounds = 1, MaxTokens = 100000, DefaultRefillTokens = 5000 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        await engine.ContinueAsync(BudgetRefillMode.RoundsOnly, ct: CancellationToken.None);

        var budget = engine.GetBudgetStatus();
        Assert.Equal(100000, budget.TokensBudget);
    }

    private static ReasoningEngine CreateEngine(ReasoningOptions? options = null)
    {
        var agents = new IReasoningAgent[]
        {
            new ProsecutorAgent(NullLogger<ProsecutorAgent>.Instance),
            new DefenderAgent(NullLogger<DefenderAgent>.Instance),
            new JudgeAgent(NullLogger<JudgeAgent>.Instance),
        };
        return new ReasoningEngine(agents, NullLogger<ReasoningEngine>.Instance, options);
    }

    private static async Task RunApplyVerdicts(ReasoningEngine engine, IReadOnlyList<Verdict> verdicts)
    {
        var method = typeof(ReasoningEngine).GetMethod("ApplyVerdicts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(engine, [verdicts]);
        await Task.CompletedTask;
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("content"),
            });
        }
    }
}
