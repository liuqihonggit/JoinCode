namespace JoinCode.Reasoning.Tests.Agents;

public sealed class ProsecutorAgentTests {
    [Fact]
    public async Task ReasonAsync_WithNoAssumptions_ReturnsEmptyAction() {
        await using var agent = new ProsecutorAgent(new FakeQueryEngine(), NullLogger<ProsecutorAgent>.Instance);
        var context = CreateContext([]);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Equal(AgentRole.Prosecutor, action.AgentRole);
        Assert.Empty(action.Evidence);
        Assert.Empty(action.AffectedClaimIds);
        Assert.Equal(0, action.TokensUsed);
    }

    [Fact]
    public async Task ReasonAsync_WithAssumptionsButNoChatClient_ReturnsActionWithAffectedClaims() {
        await using var agent = new ProsecutorAgent(new FakeQueryEngine(), NullLogger<ProsecutorAgent>.Instance);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item]);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.AffectedClaimIds);
        Assert.Equal(item.Id, action.AffectedClaimIds[0]);
        Assert.Empty(action.Evidence);
        Assert.Equal(0, action.TokensUsed);
    }

    [Fact]
    public async Task ReasonAsync_WithValidLlmResponse_ParsesEvidence() {
        var json = "{\"evidence\":[{\"content\":\"DNA匹配\",\"source\":\"实验室\",\"trustLevel\":\"DirectEvidence\",\"weight\":5.0}]}";
        await using var agent = new ProsecutorAgent(
            new FakeQueryEngine(),
            NullLogger<ProsecutorAgent>.Instance,
            new FakeChatClient(json));
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item]);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.Evidence);
        Assert.Equal("DNA匹配", action.Evidence[0].Content);
        Assert.Equal("实验室", action.Evidence[0].Source);
        Assert.Equal(TrustLevel.DirectEvidence, action.Evidence[0].TrustLevel);
        Assert.Equal(5.0, action.Evidence[0].Weight);
        Assert.Equal(AgentRole.Prosecutor, action.Evidence[0].SubmittedBy);
        Assert.Equal(EvidenceCategory.Documentary, action.Evidence[0].Category);
        Assert.True(action.TokensUsed > 0);
    }

    [Fact]
    public async Task ReasonAsync_WithMalformedJson_LogsWarningAndReturnsEmptyEvidence() {
        await using var agent = new ProsecutorAgent(
            new FakeQueryEngine(),
            NullLogger<ProsecutorAgent>.Instance,
            new FakeChatClient("这不是json"));
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item]);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Empty(action.Evidence);
        Assert.True(action.TokensUsed > 0);
    }

    [Fact]
    public async Task ReasonAsync_WithMissingEvidenceArray_ReturnsEmptyEvidence() {
        await using var agent = new ProsecutorAgent(
            new FakeQueryEngine(),
            NullLogger<ProsecutorAgent>.Instance,
            new FakeChatClient("{\"other\":\"value\"}"));
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item]);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Empty(action.Evidence);
    }

    [Fact]
    public async Task ReasonAsync_WithBroker_SendsEvidenceSubmittedMessage() {
        var broker = new FakeMessageBroker();
        await using var agent = new ProsecutorAgent(
            new FakeQueryEngine(),
            NullLogger<ProsecutorAgent>.Instance,
            new FakeChatClient("{\"evidence\":[]}"),
            broker);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item]);

        await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(broker.SentMessages);
        Assert.Equal(AgentRole.Defender.ToValue(), broker.SentMessages[0].ToAgentId);
        Assert.Equal("evidence_submitted", broker.SentMessages[0].MessageType);
    }

    [Fact]
    public async Task ReasonAsync_EvidenceItemUsesDefaults_WhenOptionalFieldsMissing() {
        var json = "{\"evidence\":[{\"content\":\"仅内容\"}]}";
        await using var agent = new ProsecutorAgent(
            new FakeQueryEngine(),
            NullLogger<ProsecutorAgent>.Instance,
            new FakeChatClient(json));
        var item = new DataItem { Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item]);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Single(action.Evidence);
        Assert.Equal("仅内容", action.Evidence[0].Content);
        Assert.Equal("LLM生成", action.Evidence[0].Source);
        Assert.Equal(TrustLevel.Moderate, action.Evidence[0].TrustLevel);
        Assert.Equal(1.0, action.Evidence[0].Weight);
    }

    [Fact]
    public async Task ParseEvidenceFromLlmResponse_ValidJson_ShouldParseAllFields() {
        await using var agent = new ProsecutorAgent(new FakeQueryEngine(), NullLogger<ProsecutorAgent>.Instance);
        var json = "{\"evidence\":[{\"content\":\"DNA匹配\",\"source\":\"实验室\",\"trustLevel\":\"DirectEvidence\",\"weight\":5.0}]}";

        var records = agent.ParseEvidenceFromLlmResponse(json);

        Assert.Single(records);
        Assert.Equal("DNA匹配", records[0].Content);
        Assert.Equal("实验室", records[0].Source);
        Assert.Equal(TrustLevel.DirectEvidence, records[0].TrustLevel);
        Assert.Equal(5.0, records[0].Weight);
        Assert.Equal(AgentRole.Prosecutor, records[0].SubmittedBy);
        Assert.Equal(EvidenceCategory.Documentary, records[0].Category);
    }

    [Fact]
    public async Task ParseEvidenceFromLlmResponse_MissingFields_ShouldUseDefaults() {
        await using var agent = new ProsecutorAgent(new FakeQueryEngine(), NullLogger<ProsecutorAgent>.Instance);
        var json = "{\"evidence\":[{\"content\":\"仅内容\"}]}";

        var records = agent.ParseEvidenceFromLlmResponse(json);

        Assert.Single(records);
        Assert.Equal("仅内容", records[0].Content);
        Assert.Equal("LLM生成", records[0].Source);
        Assert.Equal(TrustLevel.Moderate, records[0].TrustLevel);
        Assert.Equal(1.0, records[0].Weight);
    }

    [Fact]
    public async Task ParseEvidenceFromLlmResponse_NoEvidenceField_ShouldReturnEmpty() {
        await using var agent = new ProsecutorAgent(new FakeQueryEngine(), NullLogger<ProsecutorAgent>.Instance);
        var json = "{\"other\":\"value\"}";

        var records = agent.ParseEvidenceFromLlmResponse(json);

        Assert.Empty(records);
    }

    [Fact]
    public async Task ParseEvidenceFromLlmResponse_MalformedJson_ShouldReturnEmpty() {
        await using var agent = new ProsecutorAgent(new FakeQueryEngine(), NullLogger<ProsecutorAgent>.Instance);

        var records = agent.ParseEvidenceFromLlmResponse("not json");

        Assert.Empty(records);
    }

    [Fact]
    public async Task ParseEvidenceFromLlmResponse_MultipleItems_ShouldParseAll() {
        await using var agent = new ProsecutorAgent(new FakeQueryEngine(), NullLogger<ProsecutorAgent>.Instance);
        var json = "{\"evidence\":[{\"content\":\"证据1\",\"weight\":1.0},{\"content\":\"证据2\",\"weight\":2.0}]}";

        var records = agent.ParseEvidenceFromLlmResponse(json);

        Assert.Equal(2, records.Count);
        Assert.Equal("证据1", records[0].Content);
        Assert.Equal(1.0, records[0].Weight);
        Assert.Equal("证据2", records[1].Content);
        Assert.Equal(2.0, records[1].Weight);
    }

    private static ReasoningContext CreateContext(IReadOnlyList<DataItem> items) {
        return new ReasoningContext {
            AllItems = items,
            AllEvidence = [],
            Dag = new Dag<ReasoningPayload>(),
            Options = new ReasoningOptions(),
        };
    }
}