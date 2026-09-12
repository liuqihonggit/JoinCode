namespace JoinCode.Reasoning.Tests.Engine;

public sealed class ReasoningOptionsEdgeCaseTests
{
    [Fact]
    public void DefaultValues_ShouldMatchExpectedDefaults()
    {
        var opts = new ReasoningOptions();

        Assert.Equal(100, opts.MaxNodes);
        Assert.Equal(20, opts.MaxEvidencePerClaim);
        Assert.Equal(10, opts.MaxDepth);
        Assert.Equal(5, opts.ConeWindowSize);
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
        Assert.Equal(1.0, opts.DefaultEvidenceWeight);
        Assert.Equal(0.3f, opts.ProsecutorTemperature);
        Assert.Equal(0.4f, opts.DefenderTemperature);
        Assert.Equal(0.2f, opts.JudgeTemperature);
        Assert.Equal(2000, opts.DefaultLlmMaxTokens);
        Assert.Equal(4000, opts.MaxPromptTokens);
        Assert.Equal(30, opts.DagSummarizationThreshold);
        Assert.Equal(100, opts.RoundOverheadTokens);
        Assert.Equal(50, opts.DowngradedConfidence);
        Assert.Equal(10, opts.RejectedConfidence);
        Assert.Equal(1.0, opts.VerdictEdgeWeight);
    }

    [Fact]
    public void IsNodeLimitReached_AtExactLimit_ReturnsTrue()
    {
        var opts = new ReasoningOptions { MaxNodes = 5 };

        Assert.False(opts.IsNodeLimitReached(4));
        Assert.True(opts.IsNodeLimitReached(5));
    }

    [Fact]
    public void IsEvidenceLimitReached_AtExactLimit_ReturnsTrue()
    {
        var opts = new ReasoningOptions { MaxEvidencePerClaim = 3 };

        Assert.False(opts.IsEvidenceLimitReached(2));
        Assert.True(opts.IsEvidenceLimitReached(3));
    }

    [Fact]
    public void FromPreset_UnknownPreset_ReturnsPanda()
    {
        var result = ReasoningOptions.FromPreset((ReasoningPreset)999);

        Assert.Same(ReasoningOptions.Panda, result);
    }

    [Fact]
    public void Builder_FromPreset_UnknownPreset_ReturnsPandaBuilder()
    {
        var result = ReasoningOptionsBuilder.FromPreset((ReasoningPreset)999).Build();

        Assert.Equal(ReasoningOptions.Panda.MaxNodes, result.MaxNodes);
        Assert.Equal(ReasoningOptions.Panda.AcceptThreshold, result.AcceptThreshold);
    }

    [Fact]
    public void Builder_CreatePanda_ReturnsDefaultBuilder()
    {
        var result = ReasoningOptionsBuilder.CreatePanda().Build();

        Assert.Equal(ReasoningOptions.Panda.MaxNodes, result.MaxNodes);
        Assert.Equal(ReasoningOptions.Panda.AcceptThreshold, result.AcceptThreshold);
    }

    [Fact]
    public void Builder_AllMethods_ReturnSameInstanceForChaining()
    {
        var builder = ReasoningOptionsBuilder.Create();

        Assert.Same(builder, builder.WithMaxNodes(1));
        Assert.Same(builder, builder.WithMaxEvidencePerClaim(1));
        Assert.Same(builder, builder.WithMaxDepth(1));
        Assert.Same(builder, builder.WithMaxAdversarialRounds(1));
        Assert.Same(builder, builder.WithMaxTokens(1));
        Assert.Same(builder, builder.WithDefaultRefillRounds(1));
        Assert.Same(builder, builder.WithDefaultRefillTokens(1));
        Assert.Same(builder, builder.WithDefaultRefillMode(BudgetRefillMode.RoundsOnly));
        Assert.Same(builder, builder.WithAcceptThreshold(1.0));
        Assert.Same(builder, builder.WithAcceptMultiplier(1.0));
        Assert.Same(builder, builder.WithRejectMultiplier(1.0));
        Assert.Same(builder, builder.WithPendingWeightDelta(1.0));
        Assert.Same(builder, builder.WithDefenderDoubtThreshold(1));
        Assert.Same(builder, builder.WithDefaultEvidenceWeight(1.0));
    }

    [Fact]
    public void Builder_DefaultEvidenceWeight_IsReflectedInBuild()
    {
        var opts = ReasoningOptionsBuilder.Create()
            .WithDefaultEvidenceWeight(2.5)
            .Build();

        Assert.Equal(2.5, opts.DefaultEvidenceWeight);
    }

    [Fact]
    public void MurderPreset_ShouldHaveStrictValues()
    {
        var murder = ReasoningOptions.Murder;

        Assert.Equal(50, murder.MaxNodes);
        Assert.Equal(10, murder.MaxEvidencePerClaim);
        Assert.Equal(5, murder.MaxDepth);
        Assert.Equal(3, murder.MaxAdversarialRounds);
        Assert.Equal(5000, murder.MaxTokens);
        Assert.Equal(2, murder.DefaultRefillRounds);
        Assert.Equal(3000, murder.DefaultRefillTokens);
        Assert.Equal(5.0, murder.AcceptThreshold);
        Assert.Equal(2.0, murder.AcceptMultiplier);
        Assert.Equal(1.5, murder.RejectMultiplier);
        Assert.Equal(1.0, murder.PendingWeightDelta);
        Assert.Equal(3, murder.DefenderDoubtThreshold);
    }

    [Fact]
    public void DivorcePreset_ShouldHavePermissiveValues()
    {
        var divorce = ReasoningOptions.Divorce;

        Assert.Equal(500, divorce.MaxNodes);
        Assert.Equal(50, divorce.MaxEvidencePerClaim);
        Assert.Equal(20, divorce.MaxDepth);
        Assert.Equal(10, divorce.MaxAdversarialRounds);
        Assert.Equal(50000, divorce.MaxTokens);
        Assert.Equal(5, divorce.DefaultRefillRounds);
        Assert.Equal(10000, divorce.DefaultRefillTokens);
        Assert.Equal(1.5, divorce.AcceptThreshold);
        Assert.Equal(1.2, divorce.AcceptMultiplier);
        Assert.Equal(1.5, divorce.RejectMultiplier);
        Assert.Equal(0.3, divorce.PendingWeightDelta);
        Assert.Equal(1, divorce.DefenderDoubtThreshold);
    }
}
