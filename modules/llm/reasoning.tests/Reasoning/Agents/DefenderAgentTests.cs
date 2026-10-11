namespace JoinCode.Reasoning.Tests.Agents;

public sealed class DefenderAgentTests {
    [Fact]
    public async Task ReasonAsync_WithNoTargets_ReturnsEmptyAction() {
        await using var agent = new DefenderAgent(new FakeQueryEngine(), NullLogger<DefenderAgent>.Instance);
        var context = CreateContext([], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Equal(AgentRole.Defender, action.AgentRole);
        Assert.Empty(action.Doubts);
        Assert.Empty(action.CounterEvidence);
        Assert.Equal(0, action.TokensUsed);
    }

    [Fact]
    public async Task ReasonAsync_WithVerifiedItemAndInsufficientEvidence_AddsDoubt() {
        await using var agent = new DefenderAgent(new FakeQueryEngine(), NullLogger<DefenderAgent>.Instance);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Verified };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.Doubts);
        Assert.Contains("证据链不完整", action.Doubts[0]);
        Assert.Equal("质疑", action.ActionType);
        Assert.Single(action.AffectedClaimIds);
    }

    [Fact]
    public async Task ReasonAsync_WithSufficientEvidence_DoesNotAddDoubt() {
        await using var agent = new DefenderAgent(new FakeQueryEngine(), NullLogger<DefenderAgent>.Instance);
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
    public async Task ReasonAsync_WithValidLlmResponse_ParsesCounterEvidenceAndDoubts() {
        var json = "{\"counterEvidence\":[{\"content\":\"不在场证明\",\"source\":\"证人\",\"trustLevel\":\"StrongCorroboration\",\"weight\":2.5}],\"doubts\":[\"证据来源可疑\"]}";
        await using var agent = new DefenderAgent(
            new FakeQueryEngine(),
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
    public async Task ReasonAsync_WithMalformedJson_ReturnsEmptyParsedResults() {
        await using var agent = new DefenderAgent(
            new FakeQueryEngine(),
            NullLogger<DefenderAgent>.Instance,
            new FakeChatClient("invalid"));
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Empty(action.CounterEvidence);
    }

    [Fact]
    public async Task ReasonAsync_WithBroker_SendsCounterEvidenceSubmittedMessage() {
        var broker = new FakeMessageBroker();
        await using var agent = new DefenderAgent(
            new FakeQueryEngine(),
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
    public async Task ReasonAsync_CounterEvidenceDefaults_WhenOptionalFieldsMissing() {
        var json = "{\"counterEvidence\":[{\"content\":\"仅内容\"}]}";
        await using var agent = new DefenderAgent(
            new FakeQueryEngine(),
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

    [Fact]
    public async Task ParseCounterEvidenceFromLlmResponse_ValidJson_ShouldParseBothFields() {
        await using var agent = new DefenderAgent(new FakeQueryEngine(), NullLogger<DefenderAgent>.Instance);
        var json = "{\"counterEvidence\":[{\"content\":\"反驳\",\"source\":\"证人\",\"trustLevel\":\"StrongCorroboration\",\"weight\":2.5}],\"doubts\":[\"疑点1\",\"疑点2\"]}";

        var (counterEvidence, doubts) = agent.ParseCounterEvidenceFromLlmResponse(json);

        Assert.Single(counterEvidence);
        Assert.Equal("反驳", counterEvidence[0].Content);
        Assert.Equal("证人", counterEvidence[0].Source);
        Assert.Equal(TrustLevel.StrongCorroboration, counterEvidence[0].TrustLevel);
        Assert.Equal(2.5, counterEvidence[0].Weight);
        Assert.Equal(AgentRole.Defender, counterEvidence[0].SubmittedBy);
        Assert.Equal(EvidenceCategory.Documentary, counterEvidence[0].Category);
        Assert.Equal(2, doubts.Count);
        Assert.Equal("疑点1", doubts[0]);
        Assert.Equal("疑点2", doubts[1]);
    }

    [Fact]
    public async Task ParseCounterEvidenceFromLlmResponse_MissingFields_ShouldUseDefaults() {
        await using var agent = new DefenderAgent(new FakeQueryEngine(), NullLogger<DefenderAgent>.Instance);
        var json = "{\"counterEvidence\":[{\"content\":\"仅内容\"}],\"doubts\":[]}";

        var (counterEvidence, doubts) = agent.ParseCounterEvidenceFromLlmResponse(json);

        Assert.Single(counterEvidence);
        Assert.Equal("仅内容", counterEvidence[0].Content);
        Assert.Equal("LLM生成", counterEvidence[0].Source);
        Assert.Equal(TrustLevel.Moderate, counterEvidence[0].TrustLevel);
        Assert.Equal(1.0, counterEvidence[0].Weight);
        Assert.Empty(doubts);
    }

    [Fact]
    public async Task ParseCounterEvidenceFromLlmResponse_NoFields_ShouldReturnEmpty() {
        await using var agent = new DefenderAgent(new FakeQueryEngine(), NullLogger<DefenderAgent>.Instance);
        var json = "{\"other\":\"value\"}";

        var (counterEvidence, doubts) = agent.ParseCounterEvidenceFromLlmResponse(json);

        Assert.Empty(counterEvidence);
        Assert.Empty(doubts);
    }

    [Fact]
    public async Task ParseCounterEvidenceFromLlmResponse_MalformedJson_ShouldReturnEmpty() {
        await using var agent = new DefenderAgent(new FakeQueryEngine(), NullLogger<DefenderAgent>.Instance);

        var (counterEvidence, doubts) = agent.ParseCounterEvidenceFromLlmResponse("not json");

        Assert.Empty(counterEvidence);
        Assert.Empty(doubts);
    }

    [Fact]
    public async Task ParseCounterEvidenceFromLlmResponse_OnlyDoubts_ShouldParseDoubtsOnly() {
        await using var agent = new DefenderAgent(new FakeQueryEngine(), NullLogger<DefenderAgent>.Instance);
        var json = "{\"doubts\":[\"仅质疑\"]}";

        var (counterEvidence, doubts) = agent.ParseCounterEvidenceFromLlmResponse(json);

        Assert.Empty(counterEvidence);
        Assert.Single(doubts);
        Assert.Equal("仅质疑", doubts[0]);
    }

    private static ReasoningContext CreateContext(IReadOnlyList<DataItem> items, IReadOnlyList<EvidenceRecord> evidence) {
        return new ReasoningContext {
            AllItems = items,
            AllEvidence = evidence,
            Dag = new Dag<ReasoningPayload>(),
            Options = new ReasoningOptions { DefenderDoubtThreshold = 2 },
        };
    }

    private static ReasoningContext CreateContextWithDag(IReadOnlyList<DataItem> items, IReadOnlyList<EvidenceRecord> evidence) {
        var dag = new Dag<ReasoningPayload>();
        foreach (var item in items) {
            dag.AddNode(new DagNode<ReasoningPayload> {
                Id = item.Id,
                Payload = new ReasoningPayload {
                    Id = item.Id,
                    Type = ReasoningNodeType.Assumption,
                    Content = item.Content,
                    State = item.State,
                },
            });
        }

        foreach (var ev in evidence) {
            dag.AddNode(new DagNode<ReasoningPayload> {
                Id = ev.Id,
                Payload = new ReasoningPayload {
                    Id = ev.Id,
                    Type = ReasoningNodeType.Evidence,
                    Content = ev.Content,
                    State = DataState.Verified,
                    SubmittedBy = ev.SubmittedBy,
                },
            });
            dag.AddEdge(new DagEdge {
                FromId = ev.Id,
                ToId = items[0].Id,
                Label = "SUPPORTS",
                Weight = ev.Weight,
            });
        }

        return new ReasoningContext {
            AllItems = items,
            AllEvidence = evidence,
            Dag = dag,
            Options = new ReasoningOptions { DefenderDoubtThreshold = 2 },
        };
    }
}