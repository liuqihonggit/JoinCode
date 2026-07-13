namespace JoinCode.Reasoning.Weight.Calculator;

/// <summary>
/// 权重分量 — 5维客观权重计算的各维度得分
/// </summary>
public sealed class WeightComponents
{
    /// <summary>
    /// 来源可信度 (权重 30%)
    /// </summary>
    public double SourceCredibility { get; init; }

    /// <summary>
    /// 证据类型权重 (权重 25%)
    /// </summary>
    public double EvidenceTypeWeight { get; init; }

    /// <summary>
    /// 验证状态得分 (权重 20%)
    /// </summary>
    public double VerificationStatus { get; init; }

    /// <summary>
    /// 多重佐证得分 (权重 15%)
    /// </summary>
    public double CorroborationScore { get; init; }

    /// <summary>
    /// 时效性得分 (权重 10%)
    /// </summary>
    public double Timeliness { get; init; }
}

/// <summary>
/// 证据权重计算结果
/// </summary>
public sealed class EvidenceWeight
{
    /// <summary>
    /// 加权总分
    /// </summary>
    public double Total { get; init; }

    /// <summary>
    /// 各维度分量
    /// </summary>
    public WeightComponents Components { get; init; } = new();

    /// <summary>
    /// 保留LLM输出的原始分数（不作为主要依据）
    /// </summary>
    public double RawScore { get; init; }
}
