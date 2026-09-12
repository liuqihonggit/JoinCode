namespace JoinCode.Reasoning.Tests.Weight;

public sealed class WeightedDecisionSystemEdgeCaseTests
{
    [Fact]
    public void MakeWeightedDecision_WithOnlyDefenseEvidence_ReturnsDefenseWeight()
    {
        var system = new WeightedDecisionSystem();
        var defEvidence = new List<EvidenceRecord>
        {
            new() { Content = "反驳", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.DirectEvidence, SubmittedBy = AgentRole.Defender },
        };

        var result = system.MakeWeightedDecision([], defEvidence);

        Assert.Equal(0, result.ProsecutionWeight);
        Assert.True(result.DefenseWeight > 0);
    }

    [Fact]
    public void MakeWeightedDecision_FinalConfidence_IncludesBaseScoreWhenNoEvidence()
    {
        var system = new WeightedDecisionSystem();

        var result = system.MakeWeightedDecision([], []);

        Assert.Equal(0.22, result.FinalConfidence, precision: 2);
    }

    [Fact]
    public void MakeWeightedDecision_FinalConfidence_WithEvidence_HasHigherScore()
    {
        var system = new WeightedDecisionSystem();
        var pros = new List<EvidenceRecord>
        {
            new() { Content = "证据", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.DirectEvidence, SubmittedBy = AgentRole.Prosecutor },
        };

        var result = system.MakeWeightedDecision(pros, []);

        Assert.True(result.FinalConfidence > 0.25);
    }

    [Fact]
    public void MakeWeightedDecision_TopologyImpact_IsNonNegative()
    {
        var system = new WeightedDecisionSystem();
        var pros = new List<EvidenceRecord>
        {
            new() { Content = "证据", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };

        var result = system.MakeWeightedDecision(pros, []);

        Assert.True(result.TopologyImpact >= 0);
    }

    [Fact]
    public void MakeWeightedDecision_BeliefConsistency_IsNonNegative()
    {
        var system = new WeightedDecisionSystem();
        var pros = new List<EvidenceRecord>
        {
            new() { Content = "证据", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };

        var result = system.MakeWeightedDecision(pros, []);

        Assert.True(result.BeliefConsistency >= 0);
        Assert.True(result.BeliefConsistency <= 1);
    }

    [Fact]
    public void MakeWeightedDecision_ChainScores_ArePopulated()
    {
        var system = new WeightedDecisionSystem();
        var pros = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };
        var def = new List<EvidenceRecord>
        {
            new() { Content = "反驳1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Defender },
        };

        var result = system.MakeWeightedDecision(pros, def);

        Assert.Equal(1, result.ProsecutionChainScore.EvidenceCount);
        Assert.Equal(1, result.DefenseChainScore.EvidenceCount);
    }
}
