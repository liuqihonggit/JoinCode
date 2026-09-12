namespace JoinCode.Reasoning.Tests.Weight;

public sealed class TopologicalEvidenceAnalyzerTests
{
    [Fact]
    public void AnalyzeChainTopology_ShouldReturnScoreForSingleEvidence()
    {
        var analyzer = new TopologicalEvidenceAnalyzer();
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };

        var result = analyzer.AnalyzeChainTopology(chain);

        Assert.Equal(1.0, result.LengthScore);
        Assert.True(result.TotalScore > 0);
    }

    [Fact]
    public void AnalyzeChainTopology_LengthScore_ShouldDecayForLongChains()
    {
        var analyzer = new TopologicalEvidenceAnalyzer { LengthThreshold = 3 };
        var chain = Enumerable.Range(0, 10)
            .Select(i => new EvidenceRecord
            {
                Content = $"证据{i}",
                Category = EvidenceCategory.Documentary,
                TrustLevel = TrustLevel.Moderate,
                SubmittedBy = AgentRole.Prosecutor,
            })
            .ToList();

        var result = analyzer.AnalyzeChainTopology(chain);

        Assert.True(result.LengthScore < 1.0);
    }

    [Fact]
    public void AnalyzeChainTopology_IndependenceScore_ShouldScoreDiverseSourcesHigher()
    {
        var analyzer = new TopologicalEvidenceAnalyzer();
        var diverse = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "银行" },
            new() { Content = "证据2", Category = EvidenceCategory.Financial, TrustLevel = TrustLevel.DirectEvidence, SubmittedBy = AgentRole.Prosecutor, Source = "法院" },
            new() { Content = "证据3", Category = EvidenceCategory.Physical, TrustLevel = TrustLevel.StrongCorroboration, SubmittedBy = AgentRole.Prosecutor, Source = "公证处" },
        };

        var same = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "同一来源" },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "同一来源" },
            new() { Content = "证据3", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "同一来源" },
        };

        var diverseResult = analyzer.AnalyzeChainTopology(diverse);
        var sameResult = analyzer.AnalyzeChainTopology(same);

        Assert.True(diverseResult.IndependenceScore > sameResult.IndependenceScore);
    }

    [Fact]
    public void AnalyzeChainTopology_TemporalConsistency_ShouldScoreConsistentTimestampsHigher()
    {
        var analyzer = new TopologicalEvidenceAnalyzer();
        var now = DateTime.UtcNow;
        var consistent = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, CreatedAt = now },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, CreatedAt = now.AddHours(1) },
            new() { Content = "证据3", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, CreatedAt = now.AddHours(2) },
        };

        var result = analyzer.AnalyzeChainTopology(consistent);

        Assert.True(result.TemporalConsistency > 0);
    }
}
