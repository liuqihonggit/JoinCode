namespace JoinCode.Reasoning.Tests.Weight;

public sealed class WeightedDecisionSystemTests
{
    [Fact]
    public void MakeWeightedDecision_ShouldFavorStrongerProsecution()
    {
        var system = new WeightedDecisionSystem();
        var prosEvidence = new List<EvidenceRecord>
        {
            new() { Content = "直接证据", Category = EvidenceCategory.Physical, TrustLevel = TrustLevel.DirectEvidence, SubmittedBy = AgentRole.Prosecutor, Source = "法院判决" },
            new() { Content = "强佐证", Category = EvidenceCategory.Financial, TrustLevel = TrustLevel.StrongCorroboration, SubmittedBy = AgentRole.Prosecutor, Source = "银行系统" },
        };
        var defEvidence = new List<EvidenceRecord>
        {
            new() { Content = "弱反驳", Category = EvidenceCategory.Circumstantial, TrustLevel = TrustLevel.Weak, SubmittedBy = AgentRole.Defender, Source = "个人陈述" },
        };

        var result = system.MakeWeightedDecision(prosEvidence, defEvidence);

        Assert.True(result.ProsecutionWeight > result.DefenseWeight);
        Assert.True(result.FinalConfidence > 0);
    }

    [Fact]
    public void MakeWeightedDecision_ShouldReturnZeroForEmptyEvidence()
    {
        var system = new WeightedDecisionSystem();

        var result = system.MakeWeightedDecision([], []);

        Assert.Equal(0, result.ProsecutionWeight);
        Assert.Equal(0, result.DefenseWeight);
    }

    [Fact]
    public void MakeWeightedDecision_ShouldIncludeTopologyAndBeliefScores()
    {
        var system = new WeightedDecisionSystem();
        var prosEvidence = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };

        var result = system.MakeWeightedDecision(prosEvidence, []);

        Assert.True(result.TopologyImpact >= 0);
        Assert.True(result.BeliefConsistency >= 0);
    }
}
