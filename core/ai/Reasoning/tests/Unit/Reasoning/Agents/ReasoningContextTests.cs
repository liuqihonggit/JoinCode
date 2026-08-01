namespace JoinCode.Reasoning.Tests.Agents;

public sealed class ReasoningContextTests
{
    [Fact]
    public void GetConeContextForRole_WhenOrchestratorIsNull_ReturnsEmpty()
    {
        var context = CreateContext(orchestrator: null);

        var result = context.GetConeContextForRole(AgentRole.Prosecutor);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetConeContextForRole_WhenRoleConeMissing_ReturnsEmpty()
    {
        var orchestrator = new ConeOrchestrator();
        var context = CreateContext(orchestrator);

        var result = context.GetConeContextForRole(AgentRole.Prosecutor);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetConeContextForRole_WhenConeExists_ReturnsContext()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        var cone = orchestrator.GetRole(AgentRole.Prosecutor)!;
        cone.AddFragment(new ObservationFragment
        {
            FragmentId = "f1",
            SourceItemId = "item1",
            RoleChain = AgentRole.Prosecutor,
            RawText = "测试",
            Fingerprint = new CognitiveFingerprint { OutputConclusion = "结论", Confidence = 0.8 },
            FoldedSummary = "摘要",
        });
        var context = CreateContext(orchestrator);

        var result = context.GetConeContextForRole(AgentRole.Prosecutor);

        Assert.Contains("结论", result);
        Assert.Contains("摘要", result);
    }

    [Fact]
    public void GetVisibleItemsForRole_WhenOrchestratorIsNull_ReturnsAllItems()
    {
        var items = new List<DataItem> { new() { Content = "item1" } };
        var context = CreateContext(orchestrator: null, items: items);

        var result = context.GetVisibleItemsForRole(AgentRole.Prosecutor);

        Assert.Single(result);
        Assert.Equal("item1", result[0].Content);
    }

    [Fact]
    public void GetVisibleItemsForRole_WhenRoleConeMissing_ReturnsAllItems()
    {
        var items = new List<DataItem> { new() { Content = "item1" } };
        var orchestrator = new ConeOrchestrator();
        var context = CreateContext(orchestrator, items: items);

        var result = context.GetVisibleItemsForRole(AgentRole.Prosecutor);

        Assert.Single(result);
    }

    [Fact]
    public void GetVisibleItemsForRole_WhenConeExists_IncludesVisibleSourceIds()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        var cone = orchestrator.GetRole(AgentRole.Prosecutor)!;
        cone.AddFragment(new ObservationFragment
        {
            FragmentId = "f1",
            SourceItemId = "visible1",
            RoleChain = AgentRole.Prosecutor,
            RawText = "可见",
            Fingerprint = new CognitiveFingerprint(),
        });

        var items = new List<DataItem>
        {
            new() { Id = "visible1", Content = "可见项", State = DataState.Verified },
            new() { Id = "hidden1", Content = "隐藏项", State = DataState.Verified },
        };
        var context = CreateContext(orchestrator, items: items);

        var result = context.GetVisibleItemsForRole(AgentRole.Prosecutor);

        Assert.Single(result);
        Assert.Equal("visible1", result[0].Id);
    }

    [Fact]
    public void GetVisibleItemsForRole_AssumptionsAreAlwaysVisible()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        var cone = orchestrator.GetRole(AgentRole.Prosecutor)!;
        cone.AddFragment(new ObservationFragment
        {
            FragmentId = "f1",
            SourceItemId = "visible1",
            RoleChain = AgentRole.Prosecutor,
            RawText = "可见",
            Fingerprint = new CognitiveFingerprint(),
        });

        var items = new List<DataItem>
        {
            new() { Id = "visible1", Content = "可见项" },
            new() { Id = "assumption1", Content = "假定项", State = DataState.Assumption },
            new() { Id = "pending1", Content = "待证据项", State = DataState.PendingEvidence },
        };
        var context = CreateContext(orchestrator, items: items);

        var result = context.GetVisibleItemsForRole(AgentRole.Prosecutor);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void GetVisibleEvidenceForRole_WhenOrchestratorIsNull_ReturnsAllEvidence()
    {
        var evidence = new List<EvidenceRecord>
        {
            new() { Content = "ev1", Category = EvidenceCategory.Documentary, SubmittedBy = AgentRole.Prosecutor },
        };
        var context = CreateContext(orchestrator: null, evidence: evidence);

        var result = context.GetVisibleEvidenceForRole(AgentRole.Prosecutor);

        Assert.Single(result);
    }

    [Fact]
    public void GetVisibleEvidenceForRole_WhenConeExists_FiltersByVisibleSourceIds()
    {
        var orchestrator = new ConeOrchestrator();
        orchestrator.RegisterRole(AgentRole.Prosecutor, 5);
        var cone = orchestrator.GetRole(AgentRole.Prosecutor)!;
        cone.AddFragment(new ObservationFragment
        {
            FragmentId = "f1",
            SourceItemId = "ev1",
            RoleChain = AgentRole.Prosecutor,
            RawText = "可见证据",
            Fingerprint = new CognitiveFingerprint(),
        });

        var evidence = new List<EvidenceRecord>
        {
            new() { Id = "ev1", Content = "可见证据", Category = EvidenceCategory.Documentary, SubmittedBy = AgentRole.Prosecutor },
            new() { Id = "ev2", Content = "隐藏证据", Category = EvidenceCategory.Documentary, SubmittedBy = AgentRole.Prosecutor },
        };
        var context = CreateContext(orchestrator, evidence: evidence);

        var result = context.GetVisibleEvidenceForRole(AgentRole.Prosecutor);

        Assert.Single(result);
        Assert.Equal("ev1", result[0].Id);
    }

    private static ReasoningContext CreateContext(
        ConeOrchestrator? orchestrator,
        IReadOnlyList<DataItem>? items = null,
        IReadOnlyList<EvidenceRecord>? evidence = null)
    {
        return new ReasoningContext
        {
            AllItems = items ?? [],
            AllEvidence = evidence ?? [],
            Dag = new Dag<ReasoningPayload>(),
            Options = new ReasoningOptions(),
            ConeOrchestrator = orchestrator,
        };
    }
}
