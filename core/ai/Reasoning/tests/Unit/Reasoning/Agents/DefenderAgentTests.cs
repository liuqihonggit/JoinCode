namespace JoinCode.Reasoning.Tests.Agents;

public sealed class DefenderAgentTests
{
    [Fact]
    public async Task ReasonAsync_WithNoTargets_ReturnsEmptyAction()
    {
        var agent = new DefenderAgent(NullLogger<DefenderAgent>.Instance);
        var context = CreateContext([], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Equal(AgentRole.Defender, action.AgentRole);
        Assert.Empty(action.Doubts);
        Assert.Empty(action.CounterEvidence);
        Assert.Equal(0, action.TokensUsed);
    }

    [Fact]
    public async Task ReasonAsync_WithVerifiedItemAndInsufficientEvidence_AddsDoubt()
    {
        var agent = new DefenderAgent(NullLogger<DefenderAgent>.Instance);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Verified };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.Doubts);
        Assert.Contains("证据链不完整", action.Doubts[0]);
        Assert.Equal("质疑", action.ActionType);
        Assert.Single(action.AffectedClaimIds);
    }

    [Fact]
    public async Task ReasonAsync_WithSufficientEvidence_DoesNotAddDoubt()
    {
        var agent = new DefenderAgent(NullLogger<DefenderAgent>.Instance);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Verified };
        var evidence = new EvidenceRecord[]
        {
            new()
            {
                Id = "ev1",
                Content = "证据1",
                Category = EvidenceCategory.Documentary,
                TrustLevel = TrustLevel.Moderate,
                SubmittedBy = AgentRole.Prosecutor,
            },
            new()
            {
                Id = "ev2",
                Content = "证据2",
                Category = EvidenceCategory.Documentary,
                TrustLevel = TrustLevel.Moderate,
                SubmittedBy = AgentRole.Prosecutor,
            },
        };
        var context = CreateContextWithDag([item], evidence);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Empty(action.Doubts);
    }

    [Fact]
    public async Task ReasonAsync_WithValidLlmResponse_ParsesCounterEvidenceAndDoubts()
    {
        var json = "{\"counterEvidence\":[{\"content\":\"不在场证明\",\"source\":\"证人\",\"trustLevel\":\"StrongCorroboration\",\"weight\":2.5}],\"doubts\":[\"证据来源可疑\"]}";
        var agent = new DefenderAgent(
            NullLogger<DefenderAgent>.Instance,
            new FakeChatClient(json));
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var evidence = new EvidenceRecord[]
        {
            new()
            {
                Id = "ev1",
                Content = "证据1",
                Category = EvidenceCategory.Documentary,
                TrustLevel = TrustLevel.Moderate,
                SubmittedBy = AgentRole.Prosecutor,
            },
            new()
            {
                Id = "ev2",
                Content = "证据2",
                Category = EvidenceCategory.Documentary,
                TrustLevel = TrustLevel.Moderate,
                SubmittedBy = AgentRole.Prosecutor,
            },
        };
        var context = CreateContextWithDag([item], evidence);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.CounterEvidence);
        Assert.Equal("不在场证明", action.CounterEvidence[0].Content);
        Assert.Equal(TrustLevel.StrongCorroboration, action.CounterEvidence[0].TrustLevel);
        Assert.Equal(AgentRole.Defender, action.CounterEvidence[0].SubmittedBy);
        Assert.Single(action.Doubts);
        Assert.Equal("证据来源可疑", action.Doubts[0]);
    }

    [Fact]
    public async Task ReasonAsync_WithMalformedJson_ReturnsEmptyParsedResults()
    {
        var agent = new DefenderAgent(
            NullLogger<DefenderAgent>.Instance,
            new FakeChatClient("invalid"));
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Empty(action.CounterEvidence);
    }

    [Fact]
    public async Task ReasonAsync_WithBroker_SendsCounterEvidenceSubmittedMessage()
    {
        var broker = new FakeMessageBroker();
        var agent = new DefenderAgent(
            NullLogger<DefenderAgent>.Instance,
            new FakeChatClient("{\"counterEvidence\":[],\"doubts\":[]}"),
            broker);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(broker.SentMessages);
        Assert.Equal(AgentRole.Judge.ToValue(), broker.SentMessages[0].ToAgentId);
        Assert.Equal("counter_evidence_submitted", broker.SentMessages[0].MessageType);
    }

    [Fact]
    public async Task ReasonAsync_CounterEvidenceDefaults_WhenOptionalFieldsMissing()
    {
        var json = "{\"counterEvidence\":[{\"content\":\"仅内容\"}]}";
        var agent = new DefenderAgent(
            NullLogger<DefenderAgent>.Instance,
            new FakeChatClient(json));
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.CounterEvidence);
        Assert.Equal("LLM生成", action.CounterEvidence[0].Source);
        Assert.Equal(TrustLevel.Moderate, action.CounterEvidence[0].TrustLevel);
        Assert.Equal(1.0, action.CounterEvidence[0].Weight);
    }

    private static ReasoningContext CreateContext(IReadOnlyList<DataItem> items, IReadOnlyList<EvidenceRecord> evidence)
    {
        return new ReasoningContext
        {
            AllItems = items,
            AllEvidence = evidence,
            Dag = new Dag<ReasoningPayload>(),
            Options = new ReasoningOptions { DefenderDoubtThreshold = 2 },
        };
    }

    private static ReasoningContext CreateContextWithDag(IReadOnlyList<DataItem> items, IReadOnlyList<EvidenceRecord> evidence)
    {
        var dag = new Dag<ReasoningPayload>();
        foreach (var item in items)
        {
            dag.AddNode(new DagNode<ReasoningPayload>
            {
                Id = item.Id,
                Payload = new ReasoningPayload
                {
                    Id = item.Id,
                    Type = ReasoningNodeType.Assumption,
                    Content = item.Content,
                    State = item.State,
                },
            });
        }

        foreach (var ev in evidence)
        {
            dag.AddNode(new DagNode<ReasoningPayload>
            {
                Id = ev.Id,
                Payload = new ReasoningPayload
                {
                    Id = ev.Id,
                    Type = ReasoningNodeType.Evidence,
                    Content = ev.Content,
                    State = DataState.Verified,
                    SubmittedBy = ev.SubmittedBy,
                },
            });
            dag.AddEdge(new DagEdge
            {
                FromId = ev.Id,
                ToId = items[0].Id,
                Label = "SUPPORTS",
                Weight = ev.Weight,
            });
        }

        return new ReasoningContext
        {
            AllItems = items,
            AllEvidence = evidence,
            Dag = dag,
            Options = new ReasoningOptions { DefenderDoubtThreshold = 2 },
        };
    }
}
