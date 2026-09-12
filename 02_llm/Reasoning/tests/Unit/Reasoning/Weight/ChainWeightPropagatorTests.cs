namespace JoinCode.Reasoning.Tests.Weight;

public sealed class ChainWeightPropagatorTests
{
    [Fact]
    public void CalculateChainScore_ShouldReturnZeroForEmptyChain()
    {
        var propagator = new ChainWeightPropagator();

        var result = propagator.CalculateChainScore([]);

        Assert.Equal(0, result.TotalScore);
    }

    [Fact]
    public void CalculateChainScore_ShouldReturnPositiveScoreForSingleEvidence()
    {
        var propagator = new ChainWeightPropagator();
        var evidence = new EvidenceRecord
        {
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
    public void CalculateChainScore_ShouldPropagateBetweenEvidence()
    {
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
    public void CalculateChainScore_DecayFactor_ShouldAffectPropagation()
    {
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
}
