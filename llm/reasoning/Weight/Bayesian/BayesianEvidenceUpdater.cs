namespace JoinCode.Reasoning.Weight.Bayesian;

/// <summary>
/// 贝叶斯证据更新器 — 高斯共轭后验传播，让证据链权重可收敛
/// </summary>
public sealed class BayesianEvidenceUpdater
{
    private readonly Dictionary<string, Posterior> _beliefs = [];

    /// <summary>
    /// 获取所有信念
    /// </summary>
    public IReadOnlyDictionary<string, Posterior> GetAllBeliefs() => _beliefs;

    /// <summary>
    /// 贝叶斯更新证据可信度
    /// </summary>
    public Posterior UpdateBelief(string evidenceId, double likelihoodMean, double likelihoodVariance)
    {
        var prior = _beliefs.GetValueOrDefault(evidenceId, new Posterior { Mean = 0.5, Variance = 0.25 });

        var posterior = UpdateGaussian(prior, likelihoodMean, likelihoodVariance);
        _beliefs[evidenceId] = posterior;

        return posterior;
    }

    /// <summary>
    /// 从证据权重计算似然并更新
    /// </summary>
    public Posterior UpdateFromEvidence(EvidenceRecord evidence, int corroborationCount = 0)
    {
        var calculator = new Weight.Calculator.EvidenceWeightCalculator();
        var weight = calculator.CalculateWeight(evidence, corroborationCount);

        var likelihoodMean = weight.Total;
        var likelihoodVariance = 0.1;

        return UpdateBelief(evidence.Id, likelihoodMean, likelihoodVariance);
    }

    /// <summary>
    /// 传播信念到关联证据
    /// </summary>
    public void PropagateBelief(string evidenceId, double relationStrength, IReadOnlyList<string> relatedIds)
    {
        if (!_beliefs.TryGetValue(evidenceId, out var posterior)) return;

        foreach (var relatedId in relatedIds)
        {
            if (!_beliefs.TryGetValue(relatedId, out var relatedPosterior)) continue;

            var propagatedMean = posterior.Mean * relationStrength +
                                relatedPosterior.Mean * (1.0 - relationStrength);

            _beliefs[relatedId] = new Posterior
            {
                Mean = propagatedMean,
                Variance = relatedPosterior.Variance * (1.0 - relationStrength * 0.1),
            };
        }
    }

    /// <summary>
    /// 获取指定证据的信念
    /// </summary>
    public Posterior? GetBelief(string evidenceId)
    {
        return _beliefs.GetValueOrDefault(evidenceId);
    }

    /// <summary>
    /// 获取所有信念的平均方差（越低越一致）
    /// </summary>
    public double GetAverageVariance()
    {
        if (_beliefs.Count == 0) return 0.25;
        return _beliefs.Values.Average(b => b.Variance);
    }

    private static Posterior UpdateGaussian(Posterior prior, double likelihoodMean, double likelihoodVariance)
    {
        var posteriorVariance = 1.0 / (1.0 / prior.Variance + 1.0 / likelihoodVariance);
        var posteriorMean = posteriorVariance * (prior.Mean / prior.Variance + likelihoodMean / likelihoodVariance);

        return new Posterior
        {
            Mean = Math.Max(0, Math.Min(1, posteriorMean)),
            Variance = Math.Max(0.001, Math.Min(0.25, posteriorVariance)),
        };
    }
}
