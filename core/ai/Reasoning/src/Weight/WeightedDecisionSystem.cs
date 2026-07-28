namespace JoinCode.Reasoning.Weight;

using JoinCode.Reasoning.Weight.Calculator;
using JoinCode.Reasoning.Weight.Chain;
using JoinCode.Reasoning.Weight.Bayesian;
using JoinCode.Reasoning.Weight.Graph;
using JoinCode.Reasoning.Weight.Topology;

/// <summary>
/// 裁决结果 — 含加权决策的详细分解
/// </summary>
public sealed class WeightedVerdictResult
{
    /// <summary>
    /// 控方证据链评分
    /// </summary>
    public ChainScore ProsecutionChainScore { get; init; } = new();

    /// <summary>
    /// 辩方证据链评分
    /// </summary>
    public ChainScore DefenseChainScore { get; init; } = new();

    /// <summary>
    /// 控方加权总权重
    /// </summary>
    public double ProsecutionWeight { get; init; }

    /// <summary>
    /// 辩方加权总权重
    /// </summary>
    public double DefenseWeight { get; init; }

    /// <summary>
    /// 拓扑影响评分
    /// </summary>
    public double TopologyImpact { get; init; }

    /// <summary>
    /// 贝叶斯信念一致性
    /// </summary>
    public double BeliefConsistency { get; init; }

    /// <summary>
    /// 最终置信度（不依赖LLM）
    /// </summary>
    public double FinalConfidence { get; init; }
}

/// <summary>
/// 加权决策系统 — 整合5维权重、链式传播、贝叶斯更新、拓扑分析
/// </summary>
public sealed class WeightedDecisionSystem
{
    private readonly EvidenceWeightCalculator _weightCalculator = new();
    private readonly ChainWeightPropagator _propagator = new();
    private readonly BayesianEvidenceUpdater _bayesianUpdater = new();
    private readonly TopologicalEvidenceAnalyzer _topologyAnalyzer = new();

    /// <summary>
    /// 法官独立调查权重占比
    /// </summary>
    public double JudgeWeightRatio { get; init; } = 0.4;

    /// <summary>
    /// 控辩双方各占权重
    /// </summary>
    public double AdversarialWeightRatio { get; init; } = 0.3;

    /// <summary>
    /// 执行加权决策
    /// </summary>
    public WeightedVerdictResult MakeWeightedDecision(
        IReadOnlyList<EvidenceRecord> prosecutionEvidence,
        IReadOnlyList<EvidenceRecord> defenseEvidence)
    {
        var prosecutionScore = _propagator.CalculateChainScore(prosecutionEvidence);
        var defenseScore = _propagator.CalculateChainScore(defenseEvidence);

        var allEvidence = prosecutionEvidence.Concat(defenseEvidence).ToList();

        foreach (var evidence in allEvidence)
        {
            _bayesianUpdater.UpdateFromEvidence(evidence);
        }

        var topologyScore = _topologyAnalyzer.AnalyzeChainTopology(allEvidence);

        var prosWeight = prosecutionScore.TotalScore;
        var defWeight = defenseScore.TotalScore;

        var finalConfidence = CalculateFinalConfidence(
            prosWeight, defWeight,
            topologyScore.TotalScore,
            _bayesianUpdater.GetAverageVariance());

        return new WeightedVerdictResult
        {
            ProsecutionChainScore = prosecutionScore,
            DefenseChainScore = defenseScore,
            ProsecutionWeight = prosWeight,
            DefenseWeight = defWeight,
            TopologyImpact = topologyScore.TotalScore,
            BeliefConsistency = Math.Max(0, 1.0 - _bayesianUpdater.GetAverageVariance() * 2),
            FinalConfidence = finalConfidence,
        };
    }

    private static double CalculateFinalConfidence(
        double prosWeight, double defWeight,
        double topologyScore, double beliefVariance)
    {
        var weightGap = Math.Abs(prosWeight - defWeight);
        var gapScore = Math.Min(1.0, weightGap / 0.5);

        var beliefScore = Math.Max(0, 1.0 - beliefVariance * 2);

        return gapScore * 0.35 +
               beliefScore * 0.25 +
               topologyScore * 0.15 +
               (prosWeight + defWeight > 0 ? 0.25 : 0);
    }
}
