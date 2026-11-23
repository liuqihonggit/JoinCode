namespace JoinCode.Reasoning.Tests.Weight;

public sealed class ChainWeightPropagatorTests {
    [Fact]
    public void CalculateChainScore_ShouldReturnZeroForEmptyChain() {
        var propagator = new ChainWeightPropagator();

        var result = propagator.CalculateChainScore([]);

        Assert.Equal(0, result.TotalScore);
    }

    [Fact]
    public void CalculateChainScore_ShouldReturnPositiveScoreForSingleEvidence() {
        var propagator = new ChainWeightPropagator();
        var evidence = new EvidenceRecord {
            Content = "证据1",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };

        var result = propagator.CalculateChainScore([evidence]);

        Assert.True(result.TotalScore > 0);
        Assert.Equal(1, result.EvidenceCount);
    }

    [Fact]
    public void CalculateChainScore_ShouldPropagateBetweenEvidence() {
        var propagator = new ChainWeightPropagator();
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.DirectEvidence, SubmittedBy = AgentRole.Prosecutor },
            new() { Content = "证据2", Category = EvidenceCategory.Physical, TrustLevel = TrustLevel.StrongCorroboration, SubmittedBy = AgentRole.Prosecutor },
            new() { Content = "证据3", Category = EvidenceCategory.Financial, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };

        var result = propagator.CalculateChainScore(chain);

        Assert.Equal(3, result.EvidenceCount);
        Assert.Equal(3, result.IndividualScores.Count);
        Assert.True(result.TotalScore > 0);
        Assert.InRange(result.ConsistencyScore, 0, 1);
    }

    [Fact]
    public void CalculateChainScore_DecayFactor_ShouldAffectPropagation() {
        var lowDecay = new ChainWeightPropagator { DecayFactor = 0.3 };
        var highDecay = new ChainWeightPropagator { DecayFactor = 0.9 };

        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };

        var lowResult = lowDecay.CalculateChainScore(chain);
        var highResult = highDecay.CalculateChainScore(chain);

        Assert.True(highResult.IndividualScores[0] > lowResult.IndividualScores[0]);
    }

    [Fact]
    public void CalculateVariance_EmptyList_ShouldReturnZero() {
        var result = ChainWeightPropagator.CalculateVariance([]);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateVariance_SingleElement_ShouldReturnZero() {
        var result = ChainWeightPropagator.CalculateVariance([1.0]);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateVariance_AllSameElements_ShouldReturnZero() {
        var result = ChainWeightPropagator.CalculateVariance([1.0, 1.0, 1.0]);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateVariance_ThreeDistinctElements_ShouldComputePopulationVariance() {
        // [1,2,3] mean=2, var=((1-2)²+(2-2)²+(3-2)²)/3 = 2/3 ≈ 0.6667
        var result = ChainWeightPropagator.CalculateVariance([1.0, 2.0, 3.0]);
        Assert.Equal(0.6666667, result, 0.0001);
    }

    [Fact]
    public void CalculateVariance_TwoElements_ShouldComputePopulationVariance() {
        // [1,5] mean=3, var=((1-3)²+(5-3)²)/2 = 8/2 = 4
        var result = ChainWeightPropagator.CalculateVariance([1.0, 5.0]);
        Assert.Equal(4.0, result, 0.0001);
    }

    [Fact]
    public void CalculateVariance_WithZeros_ShouldComputeCorrectly() {
        // [0,0,6] mean=2, var=((4+4+16)/3)=8
        var result = ChainWeightPropagator.CalculateVariance([0.0, 0.0, 6.0]);
        Assert.Equal(8.0, result, 0.0001);
    }

    [Fact]
    public void CalculateChainScore_SingleEvidence_TotalScoreEqualsWeightTimesConsistency() {
        var propagator = new ChainWeightPropagator();
        var evidence = new EvidenceRecord {
            Content = "证据1",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.Moderate,
            SubmittedBy = AgentRole.Prosecutor,
        };

        var result = propagator.CalculateChainScore([evidence]);

        // 单证据: scores=[w], variance=0, consistency=1-(0/(w+0.001))≈1, TotalScore=w*1
        Assert.Single(result.IndividualScores);
        Assert.Equal(0, result.Variance);
        Assert.Equal(1.0, result.ConsistencyScore, 0.0001);
        Assert.Equal(result.IndividualScores[0], result.TotalScore, 0.0001);
    }

    [Fact]
    public void CalculateChainScore_TwoEvidence_ShouldPropagateForwardAndBackward() {
        var propagator = new ChainWeightPropagator { DecayFactor = 0.5 };
        // 两个相同证据,计算精确传播值
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };

        var result = propagator.CalculateChainScore(chain);

        Assert.Equal(2, result.EvidenceCount);
        Assert.Equal(2, result.IndividualScores.Count);
        // 两个证据权重相同 w, scores[0]=w + w*0.5(前向), scores[1]=w + w*0.5*0.5(后向)=w+w*0.25
        // 所以 scores[0] > scores[1](前向衰减0.5 vs 后向衰减0.25)
        Assert.True(result.IndividualScores[0] > result.IndividualScores[1]);
    }
}