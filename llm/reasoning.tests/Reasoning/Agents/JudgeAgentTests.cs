namespace JoinCode.Reasoning.Tests.Agents;

public sealed class JudgeAgentTests
{
    [Fact]
    public async Task ReasonAsync_WithNoPendingItems_ReturnsEmptyAction()
    {
        var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var context = CreateContext([], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Equal(AgentRole.Judge, action.AgentRole);
        Assert.Empty(action.Verdicts);
        Assert.Equal(0, action.TokensUsed);
    }

    [Fact]
    public async Task ReasonAsync_WithNoEvidence_ReturnsEmptyVerdicts()
    {
        var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Empty(action.Verdicts);
    }

    [Fact]
    public async Task ReasonAsync_WithStrongProsecutionEvidence_ReturnsAcceptVerdict()
    {
        var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var evidence = new[]
        {
            new EvidenceRecord
            {
                Id = "ev1",
                Content = "直接证据",
                Category = EvidenceCategory.Physical,
                TrustLevel = TrustLevel.DirectEvidence,
                SubmittedBy = AgentRole.Prosecutor,
                Weight = 5.0,
            },
            new EvidenceRecord
            {
                Id = "ev2",
                Content = "强佐证",
                Category = EvidenceCategory.Documentary,
                TrustLevel = TrustLevel.StrongCorroboration,
                SubmittedBy = AgentRole.Prosecutor,
                Weight = 3.0,
            },
        };
        var context = CreateContextWithDag([item], evidence, [], new ReasoningOptions { AcceptThreshold = 0.1, AcceptMultiplier = 1.0 });

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.Verdicts);
        Assert.Equal(item.Id, action.Verdicts[0].ClaimId);
        Assert.Equal(VerdictDecision.Accept, action.Verdicts[0].Decision);
    }

    [Fact]
    public async Task ReasonAsync_WithStrongDefenseEvidence_ReturnsRejectVerdict()
    {
        var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var evidence = new EvidenceRecord
        {
            Id = "ev1",
            Content = "强反驳",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.DirectEvidence,
            SubmittedBy = AgentRole.Defender,
            Weight = 5.0,
        };
        var context = CreateContextWithDag([item], [], [evidence]);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.Verdicts);
        Assert.Equal(VerdictDecision.Reject, action.Verdicts[0].Decision);
    }

    [Fact]
    public async Task ReasonAsync_WithBalancedEvidence_ReturnsPendingOrPartial()
    {
        var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var pros = new EvidenceRecord
        {
            Id = "ev1",
            Content = "控方证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
            Weight = 1.0,
        };
        var def = new EvidenceRecord
        {
            Id = "ev2",
            Content = "辩方证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Defender,
            Weight = 1.0,
        };
        var context = CreateContextWithDag([item], [pros], [def]);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.NotEmpty(action.Verdicts);
    }

    [Fact]
    public async Task ReasonAsync_WithLlmResponse_ParsesVerdicts()
    {
        var json = "{\"verdicts\":[{\"claimContent\":\"假定1\",\"decision\":\"Accept\",\"reason\":\"证据充分\",\"confidence\":90}]}";
        var agent = new JudgeAgent(
            new FakeQueryEngine(),
            NullLogger<JudgeAgent>.Instance,
            new FakeChatClient(json));
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.Verdicts);
        Assert.Equal(item.Id, action.Verdicts[0].ClaimId);
        Assert.Equal(VerdictDecision.Accept, action.Verdicts[0].Decision);
        Assert.Equal("证据充分", action.Verdicts[0].Reason);
        Assert.Equal(90, action.Verdicts[0].Confidence);
    }

    [Fact]
    public async Task ReasonAsync_WithMalformedLlmResponse_ReturnsEmptyParsedVerdicts()
    {
        var agent = new JudgeAgent(
            new FakeQueryEngine(),
            NullLogger<JudgeAgent>.Instance,
            new FakeChatClient("not json"));
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Empty(action.Verdicts);
    }

    [Fact]
    public async Task ReasonAsync_WithBroker_BroadcastsVerdictIssued()
    {
        var broker = new FakeMessageBroker();
        var agent = new JudgeAgent(
            new FakeQueryEngine(),
            NullLogger<JudgeAgent>.Instance,
            new FakeChatClient("{\"verdicts\":[]}"),
            broker);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(broker.BroadcastMessages);
        Assert.Equal("verdict_issued", broker.BroadcastMessages[0].MessageType);
    }

    [Fact]
    public async Task ReasonAsync_LlmVerdictDefaults_WhenOptionalFieldsMissing()
    {
        var json = "{\"verdicts\":[{\"claimContent\":\"未知\"}]}";
        var agent = new JudgeAgent(
            new FakeQueryEngine(),
            NullLogger<JudgeAgent>.Instance,
            new FakeChatClient(json));
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.Verdicts);
        Assert.Equal(string.Empty, action.Verdicts[0].ClaimId);
        Assert.Equal(VerdictDecision.Pending, action.Verdicts[0].Decision);
        Assert.Equal(string.Empty, action.Verdicts[0].Reason);
        Assert.Equal(50, action.Verdicts[0].Confidence);
    }

    private static ReasoningContext CreateContext(IReadOnlyList<DataItem> items, IReadOnlyList<EvidenceRecord> evidence)
    {
        return new ReasoningContext
        {
            AllItems = items,
            AllEvidence = evidence,
            Dag = new Dag<ReasoningPayload>(),
            Options = new ReasoningOptions(),
        };
    }

    private static ReasoningContext CreateContextWithDag(
        IReadOnlyList<DataItem> items,
        IReadOnlyList<EvidenceRecord> prosEvidence,
        IReadOnlyList<EvidenceRecord> defEvidence,
        ReasoningOptions? options = null)
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

        foreach (var ev in prosEvidence)
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

        foreach (var ev in defEvidence)
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
                Label = "REFUTES",
                Weight = ev.Weight,
            });
        }

        return new ReasoningContext
        {
            AllItems = items,
            AllEvidence = prosEvidence.Concat(defEvidence).ToList(),
            Dag = dag,
            Options = options ?? new ReasoningOptions(),
        };
    }
}
