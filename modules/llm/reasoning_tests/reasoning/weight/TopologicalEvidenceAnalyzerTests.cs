namespace JoinCode.Reasoning.Tests.Weight;

public sealed class TopologicalEvidenceAnalyzerTests {
    [Fact]
    public void AnalyzeChainTopology_ShouldReturnScoreForSingleEvidence() {
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
    public void AnalyzeChainTopology_LengthScore_ShouldDecayForLongChains() {
        var analyzer = new TopologicalEvidenceAnalyzer { LengthThreshold = 3 };
        var chain = Enumerable.Range(0, 10)
            .Select(i => new EvidenceRecord {
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
    public void AnalyzeChainTopology_IndependenceScore_ShouldScoreDiverseSourcesHigher() {
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
    public void AnalyzeChainTopology_TemporalConsistency_ShouldScoreConsistentTimestampsHigher() {
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

    [Fact]
    public void CalculateBranchingFactor_EmptyChain_ShouldReturnHalf() {
        var result = TopologicalEvidenceAnalyzer.CalculateBranchingFactor([]);
        Assert.Equal(0.5, result);
    }

    [Fact]
    public void CalculateBranchingFactor_SingleElement_ShouldReturnHalf() {
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };
        var result = TopologicalEvidenceAnalyzer.CalculateBranchingFactor(chain);
        Assert.Equal(0.5, result);
    }

    [Fact]
    public void CalculateBranchingFactor_AllSameSource_ShouldReturnOne() {
        // 2个同源: multiSourceGroups=1, min(1, 1/2*2)=1.0
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "同源" },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "同源" },
        };
        var result = TopologicalEvidenceAnalyzer.CalculateBranchingFactor(chain);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateBranchingFactor_AllDistinctSources_ShouldReturnHalf() {
        // 3个不同源: multiSourceGroups=0 → 0.5
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "A" },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "B" },
            new() { Content = "证据3", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "C" },
        };
        var result = TopologicalEvidenceAnalyzer.CalculateBranchingFactor(chain);
        Assert.Equal(0.5, result);
    }

    [Fact]
    public void CalculateBranchingFactor_MixedSources_ShouldComputeRatio() {
        // 2同源+1不同(共3): multiSourceGroups=1, min(1, 1/3*2)=0.6667
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "A" },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "A" },
            new() { Content = "证据3", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "B" },
        };
        var result = TopologicalEvidenceAnalyzer.CalculateBranchingFactor(chain);
        Assert.Equal(0.6666667, result, 0.0001);
    }

    [Fact]
    public void CalculateIndependence_EmptyChain_ShouldReturnZero() {
        var result = TopologicalEvidenceAnalyzer.CalculateIndependence([]);
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateIndependence_SingleElement_ShouldReturnOne() {
        // distinct=1, independence=1/1=1, min(1, 1.5)=1.0
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "A" },
        };
        var result = TopologicalEvidenceAnalyzer.CalculateIndependence(chain);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateIndependence_AllDistinctSources_ShouldReturnOne() {
        // distinct=3, independence=3/3=1, min(1, 1.5)=1.0
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "A" },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "B" },
            new() { Content = "证据3", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "C" },
        };
        var result = TopologicalEvidenceAnalyzer.CalculateIndependence(chain);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateIndependence_AllSameSource_ShouldComputeScaled() {
        // distinct=1, independence=1/3=0.333, min(1, 0.5)=0.5
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "A" },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "A" },
            new() { Content = "证据3", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, Source = "A" },
        };
        var result = TopologicalEvidenceAnalyzer.CalculateIndependence(chain);
        Assert.Equal(0.5, result, 0.0001);
    }

    [Fact]
    public void CalculateTemporalConsistency_EmptyChain_ShouldReturnOne() {
        var result = TopologicalEvidenceAnalyzer.CalculateTemporalConsistency([]);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateTemporalConsistency_SingleElement_ShouldReturnOne() {
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor },
        };
        var result = TopologicalEvidenceAnalyzer.CalculateTemporalConsistency(chain);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateTemporalConsistency_ConsistentGaps_ShouldReturnOne() {
        // 间隔 1h,1h: gaps=[1,1], avg=1, var=0 → 1.0
        var now = DateTime.UtcNow;
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, CreatedAt = now },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, CreatedAt = now.AddHours(1) },
            new() { Content = "证据3", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, CreatedAt = now.AddHours(2) },
        };
        var result = TopologicalEvidenceAnalyzer.CalculateTemporalConsistency(chain);
        Assert.Equal(1.0, result, 0.0001);
    }

    [Fact]
    public void CalculateTemporalConsistency_InconsistentGaps_ShouldClampToZero() {
        // 间隔 1h,9h: gaps=[1,9], avg=5, var=((16+16)/2)=16, 1-16/6=1-2.667<0 → clamp 0
        var now = DateTime.UtcNow;
        var chain = new List<EvidenceRecord>
        {
            new() { Content = "证据1", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, CreatedAt = now },
            new() { Content = "证据2", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, CreatedAt = now.AddHours(1) },
            new() { Content = "证据3", Category = EvidenceCategory.Documentary, TrustLevel = TrustLevel.Moderate, SubmittedBy = AgentRole.Prosecutor, CreatedAt = now.AddHours(10) },
        };
        var result = TopologicalEvidenceAnalyzer.CalculateTemporalConsistency(chain);
        Assert.Equal(0.0, result);
    }
}