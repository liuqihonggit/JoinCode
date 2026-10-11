// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
﻿namespace JoinCode.Reasoning.Tests.Agents;

public sealed class JudgeAgentTests {
    [Fact]
    public async Task ReasonAsync_WithNoPendingItems_ReturnsEmptyAction() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var context = CreateContext([], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Equal(AgentRole.Judge, action.AgentRole);
        Assert.Empty(action.Verdicts);
        Assert.Equal(0, action.TokensUsed);
    }

    [Fact]
    public async Task ReasonAsync_WithNoEvidence_ReturnsEmptyVerdicts() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Empty(action.Verdicts);
    }

    [Fact]
    public async Task ReasonAsync_WithStrongProsecutionEvidence_ReturnsAcceptVerdict() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
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
    public async Task ReasonAsync_WithStrongDefenseEvidence_ReturnsRejectVerdict() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var evidence = new EvidenceRecord {
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
    public async Task ReasonAsync_WithBalancedEvidence_ReturnsPendingOrPartial() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var pros = new EvidenceRecord {
            Id = "ev1",
            Content = "控方证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
            Weight = 1.0,
        };
        var def = new EvidenceRecord {
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
    public async Task ReasonAsync_WithLlmResponse_ParsesVerdicts() {
        var json = "{\"verdicts\":[{\"claimContent\":\"假定1\",\"decision\":\"Accept\",\"reason\":\"证据充分\",\"confidence\":90}]}";
        await using var agent = new JudgeAgent(
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
    public async Task ReasonAsync_WithMalformedLlmResponse_ReturnsEmptyParsedVerdicts() {
        await using var agent = new JudgeAgent(
            new FakeQueryEngine(),
            NullLogger<JudgeAgent>.Instance,
            new FakeChatClient("not json"));
        var item = new DataItem { Id = "claim1", Content = "假定1", State = DataState.Assumption };
        var context = CreateContext([item], []);

        var action = await agent.ReasonAsync(context, CancellationToken.None);

        Assert.Empty(action.Verdicts);
    }

    [Fact]
    public async Task ReasonAsync_WithBroker_BroadcastsVerdictIssued() {
        var broker = new FakeMessageBroker();
        await using var agent = new JudgeAgent(
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
    public async Task ReasonAsync_LlmVerdictDefaults_WhenOptionalFieldsMissing() {
        var json = "{\"verdicts\":[{\"claimContent\":\"未知\"}]}";
        await using var agent = new JudgeAgent(
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

    private static ReasoningContext CreateContext(IReadOnlyList<DataItem> items, IReadOnlyList<EvidenceRecord> evidence) {
        return new ReasoningContext {
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
        ReasoningOptions? options = null) {
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

        foreach (var ev in prosEvidence) {
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

        foreach (var ev in defEvidence) {
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
                Label = "REFUTES",
                Weight = ev.Weight,
            });
        }

        return new ReasoningContext {
            AllItems = items,
            AllEvidence = prosEvidence.Concat(defEvidence).ToList(),
            Dag = dag,
            Options = options ?? new ReasoningOptions(),
        };
    }

    [Theory]
    [InlineData("Accept", VerdictDecision.Accept)]
    [InlineData("Reject", VerdictDecision.Reject)]
    [InlineData("PartiallyAccept", VerdictDecision.PartiallyAccept)]
    [InlineData("Pending", VerdictDecision.Pending)]
    [InlineData("", VerdictDecision.Pending)]
    [InlineData("unknown", VerdictDecision.Pending)]
    public void ParseDecision_ShouldMapKnownValuesAndDefaultToPending(string value, VerdictDecision expected) {
        var result = JudgeAgent.ParseDecision(value);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseDecision_NullValue_ShouldReturnPending() {
        var result = JudgeAgent.ParseDecision(null);
        Assert.Equal(VerdictDecision.Pending, result);
    }

    [Fact]
    public async Task ParseVerdictsFromLlmResponse_ValidJson_ShouldParseAllFields() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var json = "{\"verdicts\":[{\"claimContent\":\"假定1\",\"decision\":\"Accept\",\"reason\":\"证据充分\",\"confidence\":90}]}";
        var pending = new List<DataItem> { new() { Id = "c1", Content = "假定1", State = DataState.Assumption } };

        var verdicts = agent.ParseVerdictsFromLlmResponse(json, pending);

        Assert.Single(verdicts);
        Assert.Equal("c1", verdicts[0].ClaimId);
        Assert.Equal(VerdictDecision.Accept, verdicts[0].Decision);
        Assert.Equal("证据充分", verdicts[0].Reason);
        Assert.Equal(90, verdicts[0].Confidence);
    }

    [Fact]
    public async Task ParseVerdictsFromLlmResponse_MissingFields_ShouldUseDefaults() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var json = "{\"verdicts\":[{\"claimContent\":\"未知\"}]}";
        var pending = new List<DataItem> { new() { Id = "c1", Content = "假定1", State = DataState.Assumption } };

        var verdicts = agent.ParseVerdictsFromLlmResponse(json, pending);

        Assert.Single(verdicts);
        Assert.Equal(string.Empty, verdicts[0].ClaimId);
        Assert.Equal(VerdictDecision.Pending, verdicts[0].Decision);
        Assert.Equal(string.Empty, verdicts[0].Reason);
        Assert.Equal(50, verdicts[0].Confidence);
    }

    [Fact]
    public async Task ParseVerdictsFromLlmResponse_NoVerdictsField_ShouldReturnEmpty() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var json = "{\"other\":\"value\"}";

        var verdicts = agent.ParseVerdictsFromLlmResponse(json, []);

        Assert.Empty(verdicts);
    }

    [Fact]
    public async Task ParseVerdictsFromLlmResponse_MalformedJson_ShouldReturnEmpty() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);

        var verdicts = agent.ParseVerdictsFromLlmResponse("not json", []);

        Assert.Empty(verdicts);
    }

    [Fact]
    public async Task ParseVerdictsFromLlmResponse_MultipleVerdicts_ShouldParseAll() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var json = "{\"verdicts\":[{\"claimContent\":\"a\",\"decision\":\"Accept\",\"confidence\":80},{\"claimContent\":\"b\",\"decision\":\"Reject\",\"confidence\":20}]}";
        var pending = new List<DataItem> {
            new() { Id = "c1", Content = "a", State = DataState.Assumption },
            new() { Id = "c2", Content = "b", State = DataState.Assumption },
        };

        var verdicts = agent.ParseVerdictsFromLlmResponse(json, pending);

        Assert.Equal(2, verdicts.Count);
        Assert.Equal("c1", verdicts[0].ClaimId);
        Assert.Equal(VerdictDecision.Accept, verdicts[0].Decision);
        Assert.Equal("c2", verdicts[1].ClaimId);
        Assert.Equal(VerdictDecision.Reject, verdicts[1].Decision);
    }

    [Fact]
    public async Task DecideWithWeightedSystem_BothEmpty_ShouldReturnNull() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "c1", Content = "假定", State = DataState.Assumption };

        var verdict = agent.DecideWithWeightedSystem(item, [], [], new ReasoningOptions());

        Assert.Null(verdict);
    }

    [Fact]
    public async Task DecideWithWeightedSystem_AcceptBranch_ShouldReturnAcceptVerdict() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "c1", Content = "假定", State = DataState.Assumption };
        var pros = new List<EvidenceRecord> {
            new() { Content = "强证据", Category = EvidenceCategory.Physical, TrustLevel = TrustLevel.DirectEvidence, SubmittedBy = AgentRole.Prosecutor, Source = "法院判决" },
        };
        var def = new List<EvidenceRecord> {
            new() { Content = "弱证据", Category = EvidenceCategory.Circumstantial, TrustLevel = TrustLevel.Weak, SubmittedBy = AgentRole.Defender, Source = "个人陈述" },
        };
        var (prosW, defW) = ComputeWeights(pros, def);
        Assert.True(prosW > defW, "前置:强证据权重应大于弱证据");
        // Accept: pros >= AcceptThreshold && pros > def*AcceptMultiplier
        var opts = new ReasoningOptions { AcceptThreshold = prosW, AcceptMultiplier = 1.0 };

        var verdict = agent.DecideWithWeightedSystem(item, pros, def, opts);

        Assert.NotNull(verdict);
        Assert.Equal(VerdictDecision.Accept, verdict!.Decision);
        Assert.Equal("c1", verdict.ClaimId);
    }

    [Fact]
    public async Task DecideWithWeightedSystem_RejectBranch_ShouldReturnRejectVerdict() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "c1", Content = "假定", State = DataState.Assumption };
        var pros = new List<EvidenceRecord> {
            new() { Content = "弱证据", Category = EvidenceCategory.Circumstantial, TrustLevel = TrustLevel.Weak, SubmittedBy = AgentRole.Prosecutor, Source = "个人陈述" },
        };
        var def = new List<EvidenceRecord> {
            new() { Content = "强证据", Category = EvidenceCategory.Physical, TrustLevel = TrustLevel.DirectEvidence, SubmittedBy = AgentRole.Defender, Source = "法院判决" },
        };
        var (prosW, defW) = ComputeWeights(pros, def);
        Assert.True(defW > prosW, "前置:辩方权重应大于控方");
        // Reject: def > pros*RejectMultiplier; Accept 不触发:AcceptThreshold 高
        var opts = new ReasoningOptions { AcceptThreshold = 100.0, RejectMultiplier = 1.0 };

        var verdict = agent.DecideWithWeightedSystem(item, pros, def, opts);

        Assert.NotNull(verdict);
        Assert.Equal(VerdictDecision.Reject, verdict!.Decision);
    }

    [Fact]
    public async Task DecideWithWeightedSystem_PendingBranch_ShouldReturnPendingVerdict() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "c1", Content = "假定", State = DataState.Assumption };
        // 同强度证据,prosW≈defW,|pros-def|≈0 < PendingWeightDelta
        var pros = new List<EvidenceRecord> {
            new() { Content = "证据", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };
        var def = new List<EvidenceRecord> {
            new() { Content = "证据", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Defender },
        };
        var (prosW, defW) = ComputeWeights(pros, def);
        var gap = Math.Abs(prosW - defW);
        // Pending: pros>0 && def>0 && |pros-def| < PendingWeightDelta
        var opts = new ReasoningOptions { AcceptThreshold = 100.0, RejectMultiplier = 100.0, PendingWeightDelta = gap + 1.0 };

        var verdict = agent.DecideWithWeightedSystem(item, pros, def, opts);

        Assert.NotNull(verdict);
        Assert.Equal(VerdictDecision.Pending, verdict!.Decision);
        Assert.Equal(50, verdict.Confidence);
    }

    [Fact]
    public async Task DecideWithWeightedSystem_PartiallyAcceptBranch_ShouldReturnPartialVerdict() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "c1", Content = "假定", State = DataState.Assumption };
        var pros = new List<EvidenceRecord> {
            new() { Content = "较强证据", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.StrongCorroboration, SubmittedBy = AgentRole.Prosecutor, Source = "银行系统" },
        };
        var def = new List<EvidenceRecord> {
            new() { Content = "中等证据", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Defender, Source = "个人陈述" },
        };
        var (prosW, defW) = ComputeWeights(pros, def);
        Assert.True(prosW > defW, "前置:控方应略占优");
        var gap = prosW - defW;
        // PartiallyAccept: pros>0 && def>0 && pros>def && pros < def*AcceptMultiplier
        // 且不触发 Pending: |pros-def| >= PendingWeightDelta
        // 且不触发 Accept: pros < def*AcceptMultiplier(严格< 满足 <= 不满足)
        var acceptMult = (prosW / defW) + 0.1; // 使 prosW < defW * acceptMult
        var opts = new ReasoningOptions { AcceptThreshold = 100.0, RejectMultiplier = 100.0, PendingWeightDelta = gap / 2.0, AcceptMultiplier = acceptMult };

        var verdict = agent.DecideWithWeightedSystem(item, pros, def, opts);

        Assert.NotNull(verdict);
        Assert.Equal(VerdictDecision.PartiallyAccept, verdict!.Decision);
    }

    [Fact]
    public async Task DecideWithWeightedSystem_AcceptBoundary_EqualThreshold_ShouldAccept() {
        await using var agent = new JudgeAgent(new FakeQueryEngine(), NullLogger<JudgeAgent>.Instance);
        var item = new DataItem { Id = "c1", Content = "假定", State = DataState.Assumption };
        var pros = new List<EvidenceRecord> {
            new() { Content = "证据", Category = EvidenceCategory.Physical, TrustLevel = TrustLevel.DirectEvidence, SubmittedBy = AgentRole.Prosecutor, Source = "法院判决" },
        };
        var (prosW, _) = ComputeWeights(pros, []);
        // 边界:prosW == AcceptThreshold(>= 满足),defW=0,prosW > 0*mult=true
        var opts = new ReasoningOptions { AcceptThreshold = prosW, AcceptMultiplier = 1.5 };

        var verdict = agent.DecideWithWeightedSystem(item, pros, [], opts);

        Assert.NotNull(verdict);
        Assert.Equal(VerdictDecision.Accept, verdict!.Decision);
    }

    private static (double Pros, double Def) ComputeWeights(IReadOnlyList<EvidenceRecord> pros, IReadOnlyList<EvidenceRecord> def) {
        var sys = new WeightedDecisionSystem();
        var r = sys.MakeWeightedDecision(pros, def);
        return (r.ProsecutionWeight, r.DefenseWeight);
    }
}