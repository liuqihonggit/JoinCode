namespace JoinCode.Reasoning.Tests.Agents;

public sealed class ReasoningContextConeFilterTests
{
    [Fact]
    public void GetVisibleItemsForRole_NoCone_ReturnsAllItems()
    {
        var items = CreateTestItems(3, DataState.Assumption);
        var context = new ReasoningContext
        {
            AllItems = items,
            AllEvidence = [],
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
            ConeOrchestrator = null,
        };

        var result = context.GetVisibleItemsForRole(AgentRole.Prosecutor);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void GetVisibleItemsForRole_WithCone_ReturnsVisibleAndUnresolved()
    {
        var cone = new ConeOrchestrator();
        cone.RegisterRole(AgentRole.Prosecutor, 5);

        var item1 = new DataItem { Id = "item1", Content = "假定1", State = DataState.Assumption, Confidence = 80 };
        var item2 = new DataItem { Id = "item2", Content = "假定2", State = DataState.Fact, Confidence = 90 };
        var item3 = new DataItem { Id = "item3", Content = "假定3", State = DataState.Assumption, Confidence = 70 };

        var fragment = cone.CreateFragmentFromItem(AgentRole.Prosecutor, item1);
        cone.GetRole(AgentRole.Prosecutor)!.AddFragment(fragment);

        var context = new ReasoningContext
        {
            AllItems = [item1, item2, item3],
            AllEvidence = [],
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
            ConeOrchestrator = cone,
        };

        var result = context.GetVisibleItemsForRole(AgentRole.Prosecutor);

        result.Should().Contain(i => i.Id == "item1", "item1 is in cone");
        result.Should().Contain(i => i.Id == "item3", "item3 is Assumption (unresolved)");
        result.Should().NotContain(i => i.Id == "item2", "item2 is Fact (resolved, not in cone)");
    }

    [Fact]
    public void GetVisibleItemsForRole_EmptyCone_ReturnsUnresolvedItems()
    {
        var cone = new ConeOrchestrator();
        cone.RegisterRole(AgentRole.Prosecutor, 5);

        var item1 = new DataItem { Id = "item1", Content = "假定1", State = DataState.Assumption, Confidence = 80 };
        var item2 = new DataItem { Id = "item2", Content = "事实1", State = DataState.Fact, Confidence = 90 };

        var context = new ReasoningContext
        {
            AllItems = [item1, item2],
            AllEvidence = [],
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
            ConeOrchestrator = cone,
        };

        var result = context.GetVisibleItemsForRole(AgentRole.Prosecutor);

        result.Should().Contain(i => i.Id == "item1");
        result.Should().NotContain(i => i.Id == "item2");
    }

    [Fact]
    public void GetVisibleEvidenceForRole_NoCone_ReturnsAllEvidence()
    {
        var evidence = CreateTestEvidence(3, AgentRole.Prosecutor);
        var context = new ReasoningContext
        {
            AllItems = [],
            AllEvidence = evidence,
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
            ConeOrchestrator = null,
        };

        var result = context.GetVisibleEvidenceForRole(AgentRole.Prosecutor);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void GetVisibleEvidenceForRole_WithCone_ReturnsOnlyVisibleEvidence()
    {
        var cone = new ConeOrchestrator();
        cone.RegisterRole(AgentRole.Prosecutor, 5);

        var ev1 = new EvidenceRecord { Id = "ev1", Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor };
        var ev2 = new EvidenceRecord { Id = "ev2", Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.StrongCorroboration, SubmittedBy = AgentRole.Prosecutor };

        var fragment = cone.CreateFragmentFromEvidence(AgentRole.Prosecutor, ev1);
        cone.GetRole(AgentRole.Prosecutor)!.AddFragment(fragment);

        var context = new ReasoningContext
        {
            AllItems = [],
            AllEvidence = [ev1, ev2],
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
            ConeOrchestrator = cone,
        };

        var result = context.GetVisibleEvidenceForRole(AgentRole.Prosecutor);

        result.Should().ContainSingle(e => e.Id == "ev1");
    }

    [Fact]
    public void GetVisibleItemsForRole_PendingEvidence_AlwaysVisible()
    {
        var cone = new ConeOrchestrator();
        cone.RegisterRole(AgentRole.Prosecutor, 5);

        var item1 = new DataItem { Id = "item1", Content = "待证1", State = DataState.PendingEvidence, Confidence = 50 };

        var context = new ReasoningContext
        {
            AllItems = [item1],
            AllEvidence = [],
            Dag = new Dag<ReasoningPayload>(),
            Options = ReasoningOptions.Panda,
            ConeOrchestrator = cone,
        };

        var result = context.GetVisibleItemsForRole(AgentRole.Prosecutor);

        result.Should().Contain(i => i.Id == "item1", "PendingEvidence is always visible");
    }

    private static List<DataItem> CreateTestItems(int count, DataState state)
    {
        return Enumerable.Range(0, count)
            .Select(i => new DataItem
            {
                Id = $"item{i}",
                Content = $"测试项{i}",
                State = state,
                Confidence = 80,
            })
            .ToList();
    }

    private static List<EvidenceRecord> CreateTestEvidence(int count, AgentRole submittedBy)
    {
        return Enumerable.Range(0, count)
            .Select(i => new EvidenceRecord
            {
                Id = $"ev{i}",
                Content = $"测试证据{i}",
                Category = EvidenceCategory.Documentary,
                TrustLevel = TrustLevel.Moderate,
                SubmittedBy = submittedBy,
            })
            .ToList();
    }
}
