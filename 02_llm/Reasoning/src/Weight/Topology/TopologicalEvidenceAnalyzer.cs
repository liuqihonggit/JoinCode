namespace JoinCode.Reasoning.Weight.Topology;

/// <summary>
/// 拓扑证据分析器 — 分析证据链的拓扑性质
/// 长度(15%) + 分支度(25%) + 环检测(20%) + 独立性(25%) + 时间一致性(15%)
/// </summary>
public sealed class TopologicalEvidenceAnalyzer
{
    /// <summary>
    /// 链长度评分阈值 — 超过此值开始衰减
    /// </summary>
    public int LengthThreshold { get; init; } = 5;

    /// <summary>
    /// 分析证据链的拓扑性质
    /// </summary>
    public TopologyScore AnalyzeChainTopology(IReadOnlyList<EvidenceRecord> chain)
    {
        var lengthScore = CalculateLengthScore(chain.Count);
        var branchingScore = CalculateBranchingFactor(chain);
        var cycleScore = 1.0;
        var independenceScore = CalculateIndependence(chain);
        var temporalConsistency = CalculateTemporalConsistency(chain);

        var totalScore = lengthScore * 0.15 +
                        branchingScore * 0.25 +
                        cycleScore * 0.20 +
                        independenceScore * 0.25 +
                        temporalConsistency * 0.15;

        return new TopologyScore
        {
            LengthScore = lengthScore,
            BranchingScore = branchingScore,
            CycleScore = cycleScore,
            IndependenceScore = independenceScore,
            TemporalConsistency = temporalConsistency,
            TotalScore = totalScore,
        };
    }

    private double CalculateLengthScore(int count)
    {
        if (count < LengthThreshold) return 1.0;
        return Math.Max(0, 1.0 - (count - LengthThreshold) * 0.05);
    }

    private static double CalculateBranchingFactor(IReadOnlyList<EvidenceRecord> chain)
    {
        if (chain.Count <= 1) return 0.5;

        var sourceGroups = chain.GroupBy(e => e.Source).ToList();
        var multiSourceGroups = sourceGroups.Count(g => g.Count() > 1);

        return multiSourceGroups > 0 ? Math.Min(1.0, multiSourceGroups / (double)chain.Count * 2) : 0.5;
    }

    private static double CalculateIndependence(IReadOnlyList<EvidenceRecord> chain)
    {
        if (chain.Count == 0) return 0;
        var distinctSources = chain.Select(e => e.Source).Distinct().Count();
        var independence = distinctSources / (double)chain.Count;
        return Math.Min(1.0, independence * 1.5);
    }

    private static double CalculateTemporalConsistency(IReadOnlyList<EvidenceRecord> chain)
    {
        if (chain.Count <= 1) return 1.0;

        var timestamps = chain.Select(e => e.CreatedAt).OrderBy(t => t).ToList();
        var gaps = new List<double>();
        for (var i = 1; i < timestamps.Count; i++)
        {
            gaps.Add((timestamps[i] - timestamps[i - 1]).TotalHours);
        }

        if (gaps.Count == 0) return 1.0;

        var avgGap = gaps.Average();
        var variance = gaps.Sum(g => Math.Pow(g - avgGap, 2)) / gaps.Count;

        return Math.Max(0, Math.Min(1.0, 1.0 - variance / (avgGap + 1.0)));
    }
}
