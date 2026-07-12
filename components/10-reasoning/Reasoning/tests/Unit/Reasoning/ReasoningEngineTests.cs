namespace JoinCode.Reasoning.Tests;

public sealed class ReasoningEngineTests
{
    [Fact]
    public async Task AddAssumptionsAsync_ShouldAddItemAsAssumption()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "测试假定", State = DataState.Assumption, Source = "测试" };

        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var all = engine.GetAllItems();
        Assert.Single(all);
        Assert.Equal(DataState.Assumption, all[0].State);
        Assert.Equal("测试假定", all[0].Content);
    }

    [Fact]
    public async Task AddAssumptionsAsync_ShouldRejectNonAssumptionState()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "测试", State = DataState.Fact, Source = "测试" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.AddAssumptionsAsync([item], CancellationToken.None));
    }

    [Fact]
    public async Task AddAssumptionsAsync_ShouldRejectDuplicateContent()
    {
        var engine = CreateEngine();
        var item1 = new DataItem { Content = "重复内容", State = DataState.Assumption, Source = "测试" };
        var item2 = new DataItem { Content = "重复内容", State = DataState.Assumption, Source = "测试" };

        await engine.AddAssumptionsAsync([item1], CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.AddAssumptionsAsync([item2], CancellationToken.None));
    }

    [Fact]
    public async Task GetFacts_ShouldReturnOnlyFacts()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };

        await engine.AddAssumptionsAsync([item], CancellationToken.None);
        Assert.Empty(engine.GetFacts());
    }

    [Fact]
    public async Task GetSummary_ShouldReturnCorrectCounts()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };

        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var summary = engine.GetSummary();
        Assert.Equal(1, summary.TotalAssumptions);
        Assert.Equal(0, summary.TotalFacts);
    }

    [Fact]
    public async Task AddEvidence_ShouldLinkToClaimViaDag()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var evidence = new EvidenceRecord
        {
            Content = "证据1",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };
        engine.AddEvidence(evidence, item.Id);

        var allEvidence = engine.GetAllEvidence();
        Assert.Single(allEvidence);

        var dag = engine.Dag;
        Assert.Equal(2, dag.Nodes.Count);
        Assert.Single(dag.Edges);
    }

    [Fact]
    public async Task RunAdversarialProcessAsync_ShouldExecuteAllAgents()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        await engine.RunAdversarialProcessAsync(CancellationToken.None);

        var summary = engine.GetSummary();
        Assert.NotNull(summary.LastRunAt);
    }

    [Fact]
    public async Task PropagateEvidenceFailure_ShouldDownstreamVerdicts()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var evidence = new EvidenceRecord
        {
            Content = "证据1",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.DirectEvidence,
            SubmittedBy = AgentRole.Prosecutor,
        };
        engine.AddEvidence(evidence, item.Id);

        engine.PropagateEvidenceFailure(evidence.Id);

        var updatedEvidence = engine.GetAllEvidence().First();
        Assert.Equal(TrustLevel.Unreliable, updatedEvidence.TrustLevel);
    }

    [Fact]
    public async Task IncrementalRecompute_ShouldReturnAffectedSubgraph()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var affected = engine.IncrementalRecompute(item.Id);
        Assert.Single(affected);
        Assert.Equal(item.Id, affected[0].Id);
    }

    [Fact]
    public async Task Dag_ShouldPreventCycleOnAddEvidence()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var evidence = new EvidenceRecord
        {
            Id = "ev1",
            Content = "证据1",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };
        engine.AddEvidence(evidence, item.Id);

        Assert.False(engine.Dag.HasCycle());
    }

    private static ReasoningEngine CreateEngine()
    {
        var agents = new IReasoningAgent[]
        {
            new ProsecutorAgent(NullLogger<ProsecutorAgent>.Instance),
            new DefenderAgent(NullLogger<DefenderAgent>.Instance),
            new JudgeAgent(NullLogger<JudgeAgent>.Instance),
        };
        return new ReasoningEngine(agents, NullLogger<ReasoningEngine>.Instance);
    }
}
