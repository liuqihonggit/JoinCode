namespace JoinCode.Reasoning.Weight.Chain;

/// <summary>
/// 证据链评分结果
/// </summary>
public sealed class ChainScore
{
    /// <summary>
    /// 链的整体评分（含一致性惩罚）
    /// </summary>
    public double TotalScore { get; init; }

    /// <summary>
    /// 每个证据的传播后得分
    /// </summary>
    public IReadOnlyList<double> IndividualScores { get; init; } = [];

    /// <summary>
    /// 得分方差
    /// </summary>
    public double Variance { get; init; }

    /// <summary>
    /// 一致性得分 [0, 1]
    /// </summary>
    public double ConsistencyScore { get; init; }

    /// <summary>
    /// 证据数量
    /// </summary>
    public int EvidenceCount { get; init; }
}
