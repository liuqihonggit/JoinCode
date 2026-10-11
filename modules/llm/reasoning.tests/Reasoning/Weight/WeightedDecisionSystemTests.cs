namespace JoinCode.Reasoning.Tests.Weight;

public sealed class WeightedDecisionSystemTests {
    [Fact]
    public void MakeWeightedDecision_ShouldFavorStrongerProsecution() {
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
    public void MakeWeightedDecision_ShouldReturnZeroForEmptyEvidence() {
        var system = new WeightedDecisionSystem();

        var result = system.MakeWeightedDecision([], []);

        Assert.Equal(0, result.ProsecutionWeight);
        Assert.Equal(0, result.DefenseWeight);
    }

    [Fact]
    public void MakeWeightedDecision_ShouldIncludeTopologyAndBeliefScores() {
        var system = new WeightedDecisionSystem();
        var prosEvidence = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };

        var result = system.MakeWeightedDecision(prosEvidence, []);

        Assert.True(result.TopologyImpact >= 0);
        Assert.True(result.BeliefConsistency >= 0);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0, 0.25)]      // 全0: gapScore=0, beliefScore=1, 无证据项=0 → 0.25
    [InlineData(0.5, 0.0, 1.0, 0.0, 1.0)]        // 全满: gapScore=1, beliefScore=1, topo=1, 有证据 → 0.35+0.25+0.15+0.25=1.0
    [InlineData(0.0, 0.0, 0.0, 0.5, 0.0)]        // beliefVariance=0.5: beliefScore=max(0,1-1)=0, 无证据 → 0
    [InlineData(0.25, 0.0, 0.0, 0.0, 0.675)]     // 单因子pros: gap=0.25,gapScore=0.5 → 0.175+0.25+0+0.25=0.675
    [InlineData(0.0, 0.0, 0.5, 0.0, 0.325)]      // 单因子topo: 0+0.25+0.075+0=0.325
    [InlineData(1.0, 0.0, 0.0, 0.0, 0.85)]       // gap=1,gapScore=min(1,2)=1 → 0.35+0.25+0+0.25=0.85
    [InlineData(0.0, 0.0, 0.0, 0.6, 0.0)]        // beliefVariance=0.6 超限: beliefScore=max(0,1-1.2)=0 → 0
    [InlineData(0.3, 0.3, 0.0, 0.0, 0.5)]        // pros==def: gap=0,gapScore=0, 有证据项0.25 → 0+0.25+0+0.25=0.5
    public void CalculateFinalConfidence_ShouldComputeWeightedSum(double pros, double defW, double topo, double variance, double expected) {
        var result = WeightedDecisionSystem.CalculateFinalConfidence(pros, defW, topo, variance);
        Assert.Equal(expected, result, 0.0001);
    }

    [Fact]
    public void CalculateFinalConfidence_GapScore_ShouldClampToOne() {
        // gap=10, gapScore=min(1, 10/0.5)=1
        var result = WeightedDecisionSystem.CalculateFinalConfidence(10.0, 0.0, 0.0, 0.0);
        // gapScore=1, beliefScore=1, 有证据 → 0.35+0.25+0+0.25=0.85
        Assert.Equal(0.85, result, 0.0001);
    }

    [Fact]
    public void CalculateFinalConfidence_BeliefScore_ShouldClampToZero() {
        // beliefVariance=10, beliefScore=max(0, 1-20)=0
        var result = WeightedDecisionSystem.CalculateFinalConfidence(0.5, 0.0, 0.0, 10.0);
        // gapScore=1, beliefScore=0, 有证据 → 0.35+0+0+0.25=0.60
        Assert.Equal(0.60, result, 0.0001);
    }

    [Fact]
    public void CalculateFinalConfidence_NoEvidenceFlag_ShouldBeZeroWhenBothWeightsZero() {
        // pros+def=0 → 无证据项=0
        var result = WeightedDecisionSystem.CalculateFinalConfidence(0.0, 0.0, 1.0, 0.0);
        // gapScore=0, beliefScore=1, topo=1, 无证据 → 0+0.25+0.15+0=0.40
        Assert.Equal(0.40, result, 0.0001);
    }
}