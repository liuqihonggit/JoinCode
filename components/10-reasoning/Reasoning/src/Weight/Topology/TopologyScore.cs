namespace JoinCode.Reasoning.Weight.Topology;

/// <summary>
/// 拓扑评分结果
/// </summary>
public sealed class TopologyScore
{
    /// <summary>
    /// 链长度评分 — 过长的链可能不可靠
    /// </summary>
    public double LengthScore { get; init; }

    /// <summary>
    /// 分支度评分 — 交叉验证越多越可靠
    /// </summary>
    public double BranchingScore { get; init; }

    /// <summary>
    /// 环检测评分 — 存在循环论证会降低信任度
    /// </summary>
    public double CycleScore { get; init; }

    /// <summary>
    /// 证据独立性评分
    /// </summary>
    public double IndependenceScore { get; init; }

    /// <summary>
    /// 时间一致性评分
    /// </summary>
    public double TemporalConsistency { get; init; }

    /// <summary>
    /// 综合评分
    /// </summary>
    public double TotalScore { get; init; }
}
