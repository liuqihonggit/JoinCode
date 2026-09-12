namespace JoinCode.Reasoning.Weight.Chain;

/// <summary>
/// 链式权重传播器 — PageRank风格衰减 + 双向传播
/// 前向传播衰减因子 decayFactor，后向传播衰减因子 decayFactor * 0.5
/// </summary>
public sealed class ChainWeightPropagator
{
    private readonly EvidenceWeightCalculator _calculator = new();

    /// <summary>
    /// 默认衰减因子
    /// </summary>
    public double DecayFactor { get; init; } = 0.7;

    /// <summary>
    /// 计算证据链的传播评分
    /// </summary>
    public ChainScore CalculateChainScore(IReadOnlyList<EvidenceRecord> chain, int? corroborationCount = null)
    {
        if (chain.Count == 0) return new ChainScore { TotalScore = 0 };

        var evidenceWeights = chain
            .Select(e => _calculator.CalculateWeight(e, corroborationCount ?? 0))
            .ToList();

        var scores = new List<double>();

        for (var i = 0; i < evidenceWeights.Count; i++)
        {
            var propagatedScore = evidenceWeights[i].Total;

            for (var j = i + 1; j < evidenceWeights.Count; j++)
            {
                var distance = j - i;
                propagatedScore += evidenceWeights[j].Total * Math.Pow(DecayFactor, distance);
            }

            for (var j = i - 1; j >= 0; j--)
            {
                var distance = i - j;
                propagatedScore += evidenceWeights[j].Total * Math.Pow(DecayFactor, distance) * 0.5;
            }

            scores.Add(propagatedScore);
        }

        var finalScore = scores.Average();
        var variance = CalculateVariance(scores);
        var consistency = 1.0 - (variance / (scores.Average() + 0.001));
        consistency = Math.Max(0, Math.Min(1, consistency));

        return new ChainScore
        {
            TotalScore = finalScore * consistency,
            IndividualScores = scores,
            Variance = variance,
            ConsistencyScore = consistency,
            EvidenceCount = chain.Count,
        };
    }

    private static double CalculateVariance(List<double> scores)
    {
        if (scores.Count <= 1) return 0;
        var mean = scores.Average();
        return scores.Sum(s => Math.Pow(s - mean, 2)) / scores.Count;
    }
}
