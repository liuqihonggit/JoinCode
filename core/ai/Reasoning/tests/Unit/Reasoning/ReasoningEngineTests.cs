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

    [Fact]
    public async Task AddAssumptionsAsync_ShouldRejectWhenNodeLimitReached()
    {
        var options = new ReasoningOptions { MaxNodes = 2 };
        var engine = CreateEngine(options);
        var item1 = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        var item2 = new DataItem { Content = "假定2", State = DataState.Assumption, Source = "测试" };
        var item3 = new DataItem { Content = "假定3", State = DataState.Assumption, Source = "测试" };

        await engine.AddAssumptionsAsync([item1], CancellationToken.None);
        await engine.AddAssumptionsAsync([item2], CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.AddAssumptionsAsync([item3], CancellationToken.None));
    }

    [Fact]
    public async Task AddEvidence_ShouldRejectWhenEvidenceLimitReached()
    {
        var options = new ReasoningOptions { MaxEvidencePerClaim = 1 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var ev1 = new EvidenceRecord
        {
            Content = "证据1",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };
        engine.AddEvidence(ev1, item.Id);

        var ev2 = new EvidenceRecord
        {
            Content = "证据2",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };
        engine.AddEvidence(ev2, item.Id);

        var allEvidence = engine.GetAllEvidence();
        Assert.Single(allEvidence);
    }

    [Fact]
    public async Task RunAdversarialProcessAsync_ShouldStopWhenRoundsBudgetExhausted()
    {
        var options = new ReasoningOptions { MaxAdversarialRounds = 1, MaxTokens = 100000 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var budget1 = engine.GetBudgetStatus();
        Assert.Equal(1, budget1.RoundsUsed);

        await engine.RunAdversarialProcessAsync(CancellationToken.None);

        var budget2 = engine.GetBudgetStatus();
        Assert.Equal(1, budget2.RoundsUsed);
        Assert.True(budget2.IsRoundsExhausted);
    }

    [Fact]
    public async Task RunAdversarialProcessAsync_ShouldStopWhenTokenBudgetExhausted()
    {
        var options = new ReasoningOptions { MaxAdversarialRounds = 100, MaxTokens = 100 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var budget = engine.GetBudgetStatus();
        Assert.True(budget.IsAnyExhausted);
    }

    [Fact]
    public async Task ContinueAsync_ShouldRefillRoundsAndContinue()
    {
        var options = new ReasoningOptions { MaxAdversarialRounds = 1, MaxTokens = 100000, DefaultRefillRounds = 2 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var budget1 = engine.GetBudgetStatus();
        Assert.True(budget1.IsRoundsExhausted);

        await engine.ContinueAsync(BudgetRefillMode.RoundsOnly, ct: CancellationToken.None);

        var budget2 = engine.GetBudgetStatus();
        Assert.Equal(3, budget2.RoundsBudget);
        Assert.Equal(2, budget2.RoundsUsed);
    }

    [Fact]
    public async Task ContinueAsync_ShouldRefillTokensAndContinue()
    {
        var options = new ReasoningOptions { MaxAdversarialRounds = 100, MaxTokens = 100, DefaultRefillTokens = 5000 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var budget1 = engine.GetBudgetStatus();
        Assert.True(budget1.IsTokensExhausted);

        await engine.ContinueAsync(BudgetRefillMode.TokensOnly, ct: CancellationToken.None);

        var budget2 = engine.GetBudgetStatus();
        Assert.Equal(5100, budget2.TokensBudget);
    }

    [Fact]
    public async Task ContinueAsync_DefaultMode_ShouldRefillOnlyExhaustedBudget()
    {
        var options = new ReasoningOptions { MaxAdversarialRounds = 1, MaxTokens = 100000, DefaultRefillRounds = 3, DefaultRefillTokens = 5000 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var budget1 = engine.GetBudgetStatus();
        Assert.True(budget1.IsRoundsExhausted);
        Assert.False(budget1.IsTokensExhausted);

        await engine.ContinueAsync(BudgetRefillMode.Default, ct: CancellationToken.None);

        var budget2 = engine.GetBudgetStatus();
        Assert.Equal(4, budget2.RoundsBudget);
        Assert.Equal(100000, budget2.TokensBudget);
    }

    [Fact]
    public async Task ContinueAsync_BothMode_ShouldRefillBothBudgets()
    {
        var options = new ReasoningOptions { MaxAdversarialRounds = 1, MaxTokens = 100000, DefaultRefillRounds = 3, DefaultRefillTokens = 5000 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        await engine.ContinueAsync(BudgetRefillMode.Both, ct: CancellationToken.None);

        var budget = engine.GetBudgetStatus();
        Assert.Equal(4, budget.RoundsBudget);
        Assert.Equal(105000, budget.TokensBudget);
    }

    [Fact]
    public async Task AddEvidence_ShouldRejectWhenNodeLimitReached()
    {
        var options = new ReasoningOptions { MaxNodes = 2 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var ev1 = new EvidenceRecord
        {
            Content = "证据1",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };
        engine.AddEvidence(ev1, item.Id);

        var ev2 = new EvidenceRecord
        {
            Content = "证据2",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };
        engine.AddEvidence(ev2, item.Id);

        var allEvidence = engine.GetAllEvidence();
        Assert.Single(allEvidence);
    }

    [Fact]
    public void ReasoningOptions_Panda_ShouldHaveExpectedValues()
    {
        var opts = ReasoningOptions.Panda;
        Assert.Equal(100, opts.MaxNodes);
        Assert.Equal(20, opts.MaxEvidencePerClaim);
        Assert.Equal(10, opts.MaxDepth);
        Assert.Equal(5, opts.MaxAdversarialRounds);
        Assert.Equal(10000, opts.MaxTokens);
        Assert.Equal(3, opts.DefaultRefillRounds);
        Assert.Equal(5000, opts.DefaultRefillTokens);
        Assert.Equal(BudgetRefillMode.Both, opts.DefaultRefillMode);
        Assert.Equal(3.0, opts.AcceptThreshold);
        Assert.Equal(1.5, opts.AcceptMultiplier);
        Assert.Equal(1.2, opts.RejectMultiplier);
        Assert.Equal(0.5, opts.PendingWeightDelta);
        Assert.Equal(2, opts.DefenderDoubtThreshold);
    }

    [Fact]
    public void ReasoningOptionsBuilder_ShouldBuildCorrectOptions()
    {
        var opts = ReasoningOptionsBuilder.Create()
            .WithMaxNodes(50)
            .WithMaxEvidencePerClaim(10)
            .WithMaxDepth(5)
            .WithMaxAdversarialRounds(3)
            .WithMaxTokens(5000)
            .WithDefaultRefillRounds(2)
            .WithDefaultRefillTokens(3000)
            .WithDefaultRefillMode(BudgetRefillMode.RoundsOnly)
            .WithAcceptThreshold(5.0)
            .WithAcceptMultiplier(2.0)
            .WithRejectMultiplier(1.5)
            .WithPendingWeightDelta(1.0)
            .WithDefenderDoubtThreshold(3)
            .Build();

        Assert.Equal(50, opts.MaxNodes);
        Assert.Equal(10, opts.MaxEvidencePerClaim);
        Assert.Equal(5, opts.MaxDepth);
        Assert.Equal(3, opts.MaxAdversarialRounds);
        Assert.Equal(5000, opts.MaxTokens);
        Assert.Equal(2, opts.DefaultRefillRounds);
        Assert.Equal(3000, opts.DefaultRefillTokens);
        Assert.Equal(BudgetRefillMode.RoundsOnly, opts.DefaultRefillMode);
        Assert.Equal(5.0, opts.AcceptThreshold);
        Assert.Equal(2.0, opts.AcceptMultiplier);
        Assert.Equal(1.5, opts.RejectMultiplier);
        Assert.Equal(1.0, opts.PendingWeightDelta);
        Assert.Equal(3, opts.DefenderDoubtThreshold);
    }

    [Fact]
    public void ReasoningOptions_Murder_ShouldBeMoreRestrictive()
    {
        var murder = ReasoningOptions.Murder;
        Assert.True(murder.MaxNodes < ReasoningOptions.Panda.MaxNodes);
        Assert.True(murder.MaxEvidencePerClaim < ReasoningOptions.Panda.MaxEvidencePerClaim);
        Assert.True(murder.MaxTokens < ReasoningOptions.Panda.MaxTokens);
        Assert.True(murder.AcceptThreshold > ReasoningOptions.Panda.AcceptThreshold);
    }

    [Fact]
    public void ReasoningOptions_Divorce_ShouldBeMorePermissive()
    {
        var divorce = ReasoningOptions.Divorce;
        Assert.True(divorce.MaxNodes > ReasoningOptions.Panda.MaxNodes);
        Assert.True(divorce.MaxEvidencePerClaim > ReasoningOptions.Panda.MaxEvidencePerClaim);
        Assert.True(divorce.MaxTokens > ReasoningOptions.Panda.MaxTokens);
        Assert.True(divorce.AcceptThreshold < ReasoningOptions.Panda.AcceptThreshold);
    }

    [Fact]
    public void ReasoningOptions_IsNodeLimitReached_ShouldWorkCorrectly()
    {
        var opts = new ReasoningOptions { MaxNodes = 5 };
        Assert.False(opts.IsNodeLimitReached(4));
        Assert.True(opts.IsNodeLimitReached(5));
        Assert.True(opts.IsNodeLimitReached(6));
    }

    [Fact]
    public void ReasoningOptions_IsEvidenceLimitReached_ShouldWorkCorrectly()
    {
        var opts = new ReasoningOptions { MaxEvidencePerClaim = 3 };
        Assert.False(opts.IsEvidenceLimitReached(2));
        Assert.True(opts.IsEvidenceLimitReached(3));
        Assert.True(opts.IsEvidenceLimitReached(4));
    }

    [Fact]
    public void BudgetStatus_ShouldDetectRoundsExhaustion()
    {
        var status = new BudgetStatus { RoundsUsed = 5, RoundsBudget = 5, TokensUsed = 100, TokensBudget = 10000 };
        Assert.True(status.IsRoundsExhausted);
        Assert.False(status.IsTokensExhausted);
        Assert.True(status.IsAnyExhausted);
        Assert.Equal(BudgetExhaustionCause.Rounds, status.ExhaustionCause);
    }

    [Fact]
    public void BudgetStatus_ShouldDetectTokensExhaustion()
    {
        var status = new BudgetStatus { RoundsUsed = 1, RoundsBudget = 5, TokensUsed = 10000, TokensBudget = 10000 };
        Assert.False(status.IsRoundsExhausted);
        Assert.True(status.IsTokensExhausted);
        Assert.True(status.IsAnyExhausted);
        Assert.Equal(BudgetExhaustionCause.Tokens, status.ExhaustionCause);
    }

    [Fact]
    public void BudgetStatus_ShouldDetectBothExhaustion()
    {
        var status = new BudgetStatus { RoundsUsed = 5, RoundsBudget = 5, TokensUsed = 10000, TokensBudget = 10000 };
        Assert.True(status.IsRoundsExhausted);
        Assert.True(status.IsTokensExhausted);
        Assert.Equal(BudgetExhaustionCause.Both, status.ExhaustionCause);
    }

    [Fact]
    public void BudgetStatus_ShouldReportNoneWhenNotExhausted()
    {
        var status = new BudgetStatus { RoundsUsed = 1, RoundsBudget = 5, TokensUsed = 100, TokensBudget = 10000 };
        Assert.False(status.IsAnyExhausted);
        Assert.Equal(BudgetExhaustionCause.None, status.ExhaustionCause);
    }

    [Fact]
    public void BudgetStatus_ShouldCalculateRemaining()
    {
        var status = new BudgetStatus { RoundsUsed = 3, RoundsBudget = 5, TokensUsed = 2000, TokensBudget = 10000 };
        Assert.Equal(2, status.RoundsRemaining);
        Assert.Equal(8000, status.TokensRemaining);
    }

    [Fact]
    public void ReasoningOptionsBuilder_CreateMurder_ShouldMatchMurderPreset()
    {
        var built = ReasoningOptionsBuilder.CreateMurder().Build();
        var preset = ReasoningOptions.Murder;
        Assert.Equal(preset.MaxNodes, built.MaxNodes);
        Assert.Equal(preset.MaxEvidencePerClaim, built.MaxEvidencePerClaim);
        Assert.Equal(preset.MaxDepth, built.MaxDepth);
        Assert.Equal(preset.MaxAdversarialRounds, built.MaxAdversarialRounds);
        Assert.Equal(preset.MaxTokens, built.MaxTokens);
        Assert.Equal(preset.AcceptThreshold, built.AcceptThreshold);
    }

    [Fact]
    public void ReasoningOptionsBuilder_CreateDivorce_ShouldMatchDivorcePreset()
    {
        var built = ReasoningOptionsBuilder.CreateDivorce().Build();
        var preset = ReasoningOptions.Divorce;
        Assert.Equal(preset.MaxNodes, built.MaxNodes);
        Assert.Equal(preset.MaxEvidencePerClaim, built.MaxEvidencePerClaim);
        Assert.Equal(preset.MaxDepth, built.MaxDepth);
        Assert.Equal(preset.MaxAdversarialRounds, built.MaxAdversarialRounds);
        Assert.Equal(preset.MaxTokens, built.MaxTokens);
        Assert.Equal(preset.AcceptThreshold, built.AcceptThreshold);
    }

    [Fact]
    public async Task GetBudgetStatus_ShouldTrackTokenUsage()
    {
        var options = new ReasoningOptions { MaxAdversarialRounds = 100, MaxTokens = 100000 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var budget = engine.GetBudgetStatus();
        Assert.True(budget.TokensUsed > 0);
        Assert.True(budget.RoundsUsed > 0);
    }

    [Fact]
    public async Task ContinueAsync_WithCustomAmounts_ShouldOverrideDefaults()
    {
        var options = new ReasoningOptions { MaxAdversarialRounds = 1, MaxTokens = 100000, DefaultRefillRounds = 3, DefaultRefillTokens = 5000 };
        var engine = CreateEngine(options);
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        await engine.ContinueAsync(BudgetRefillMode.Both, extraRounds: 10, extraTokens: 20000, ct: CancellationToken.None);

        var budget = engine.GetBudgetStatus();
        Assert.Equal(11, budget.RoundsBudget);
        Assert.Equal(120000, budget.TokensBudget);
    }

    [Fact]
    public void ReasoningOptions_FromPreset_ShouldReturnCorrectOptions()
    {
        Assert.Same(ReasoningOptions.Murder, ReasoningOptions.FromPreset(ReasoningPreset.Murder));
        Assert.Same(ReasoningOptions.Panda, ReasoningOptions.FromPreset(ReasoningPreset.Panda));
        Assert.Same(ReasoningOptions.Divorce, ReasoningOptions.FromPreset(ReasoningPreset.Divorce));
    }

    [Fact]
    public void ReasoningOptionsBuilder_FromPreset_ShouldReturnCorrectBuilder()
    {
        var murderBuilt = ReasoningOptionsBuilder.FromPreset(ReasoningPreset.Murder).Build();
        Assert.Equal(ReasoningOptions.Murder.MaxNodes, murderBuilt.MaxNodes);
        Assert.Equal(ReasoningOptions.Murder.AcceptThreshold, murderBuilt.AcceptThreshold);

        var divorceBuilt = ReasoningOptionsBuilder.FromPreset(ReasoningPreset.Divorce).Build();
        Assert.Equal(ReasoningOptions.Divorce.MaxNodes, divorceBuilt.MaxNodes);
        Assert.Equal(ReasoningOptions.Divorce.AcceptThreshold, divorceBuilt.AcceptThreshold);
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

    [Fact]
    public async Task DetectConeConflict_ShouldReturnConflictResult()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var result = engine.DetectConeConflict(AgentRole.Prosecutor, AgentRole.Defender);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ExpandFragment_ShouldExpandWithMatchingCondition()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        var cone = engine.ConeOrchestrator.GetRole(AgentRole.Prosecutor);
        Assert.NotNull(cone);
        Assert.True(cone.AllFragments.Count > 0);

        var firstFragment = cone.AllFragments.Values.First();
        var expanded = engine.ExpandFragment(AgentRole.Prosecutor, firstFragment.FragmentId, "cross_role_review");

        Assert.NotNull(expanded);
    }

    [Fact]
    public async Task ConeOrchestrator_ShouldHaveAllRolesRegistered()
    {
        var engine = CreateEngine();

        Assert.NotNull(engine.ConeOrchestrator.GetRole(AgentRole.Prosecutor));
        Assert.NotNull(engine.ConeOrchestrator.GetRole(AgentRole.Defender));
        Assert.NotNull(engine.ConeOrchestrator.GetRole(AgentRole.Judge));
    }

    [Fact]
    public async Task BayesianUpdater_ShouldBeAccessible()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        Assert.NotNull(engine.BayesianUpdater);
    }

    [Fact]
    public async Task AddAssumptionsAsync_ShouldCreateFragmentsInAllCones()
    {
        var engine = CreateEngine();
        var item = new DataItem { Content = "假定1", State = DataState.Assumption, Source = "测试" };
        await engine.AddAssumptionsAsync([item], CancellationToken.None);

        foreach (AgentRole role in Enum.GetValues<AgentRole>())
        {
            var cone = engine.ConeOrchestrator.GetRole(role);
            Assert.NotNull(cone);
            Assert.True(cone.AllFragments.Count > 0);
        }
    }

    [Fact]
    public async Task ApplyVerdicts_PartiallyAccept_ShouldSetVerifiedState()
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

        var counterEvidence = new EvidenceRecord
        {
            Content = "反驳1",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Defender,
        };
        engine.AddCounterEvidence(counterEvidence, item.Id);
    }

    [Fact]
    public void ReasoningOptions_ConeWindowSize_ShouldDefaultTo5()
    {
        var opts = ReasoningOptions.Panda;
        Assert.Equal(5, opts.ConeWindowSize);
    }
}
