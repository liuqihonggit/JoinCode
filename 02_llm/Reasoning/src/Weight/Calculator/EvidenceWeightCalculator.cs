namespace JoinCode.Reasoning.Weight.Calculator;

/// <summary>
/// 证据权重计算器 — 5维客观权重计算，不依赖LLM主观评分
/// 来源可信度(30%) + 证据类型(25%) + 验证状态(20%) + 多重佐证(15%) + 时效性(10%)
/// </summary>
public sealed class EvidenceWeightCalculator
{
    private static readonly FrozenDictionary<string, double> SourceCredibilityMap = new Dictionary<string, double>
    {
        ["政府机构"] = 0.95,
        ["法院判决"] = 0.90,
        ["银行系统"] = 0.88,
        ["公证文件"] = 0.85,
        ["媒体报道"] = 0.60,
        ["个人陈述"] = 0.40,
        ["匿名来源"] = 0.15,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<EvidenceCategory, double> EvidenceTypeWeightMap = new Dictionary<EvidenceCategory, double>
    {
        [EvidenceCategory.JudicialNotice] = 0.95,
        [EvidenceCategory.Physical] = 0.90,
        [EvidenceCategory.Documentary] = 0.85,
        [EvidenceCategory.Financial] = 0.80,
        [EvidenceCategory.Contractual] = 0.80,
        [EvidenceCategory.ExpertOpinion] = 0.70,
        [EvidenceCategory.Digital] = 0.70,
        [EvidenceCategory.Testimonial] = 0.55,
        [EvidenceCategory.Circumstantial] = 0.40,
    }.ToFrozenDictionary();

    /// <summary>
    /// 计算证据的客观权重
    /// </summary>
    public EvidenceWeight CalculateWeight(EvidenceRecord evidence, int corroborationCount = 0)
    {
        var sourceCredibility = CalculateSourceCredibility(evidence.Source);
        var evidenceTypeWeight = GetTypeWeight(evidence.Category);
        var verificationStatus = CalculateVerificationScore(evidence);
        var corroborationScore = CalculateCorroboration(corroborationCount);
        var timeliness = CalculateTimeliness(evidence.CreatedAt);

        var total = sourceCredibility * 0.30 +
                    evidenceTypeWeight * 0.25 +
                    verificationStatus * 0.20 +
                    corroborationScore * 0.15 +
                    timeliness * 0.10;

        return new EvidenceWeight
        {
            Total = total,
            Components = new WeightComponents
            {
                SourceCredibility = sourceCredibility,
                EvidenceTypeWeight = evidenceTypeWeight,
                VerificationStatus = verificationStatus,
                CorroborationScore = corroborationScore,
                Timeliness = timeliness,
            },
            RawScore = (int)evidence.TrustLevel / 100.0,
        };
    }

    /// <summary>
    /// 批量计算并返回加权总分
    /// </summary>
    public double CalculateTotalWeight(IReadOnlyList<EvidenceRecord> evidence, Func<EvidenceRecord, int> corroborationLookup)
    {
        return evidence.Sum(e => CalculateWeight(e, corroborationLookup(e)).Total * e.Weight);
    }

    private static double CalculateSourceCredibility(string? source)
    {
        if (string.IsNullOrEmpty(source)) return 0.30;
        return SourceCredibilityMap.GetValueOrDefault(source, 0.30);
    }

    private static double GetTypeWeight(EvidenceCategory category)
    {
        return EvidenceTypeWeightMap.GetValueOrDefault(category, 0.30);
    }

    private static double CalculateVerificationScore(EvidenceRecord evidence)
    {
        if (evidence.IsUrlVerified) return 1.0;
        if (!string.IsNullOrEmpty(evidence.SourceUrl)) return 0.5;
        return 0.7;
    }

    private static double CalculateCorroboration(int corroborationCount)
    {
        if (corroborationCount >= 3) return 1.0;
        if (corroborationCount >= 2) return 0.8;
        if (corroborationCount >= 1) return 0.5;
        return 0.2;
    }

    private static double CalculateTimeliness(DateTime createdAt)
    {
        var age = DateTime.UtcNow - createdAt;
        if (age.TotalDays <= 7) return 1.0;
        if (age.TotalDays <= 30) return 0.9;
        if (age.TotalDays <= 90) return 0.7;
        if (age.TotalDays <= 365) return 0.5;
        return 0.3;
    }
}
