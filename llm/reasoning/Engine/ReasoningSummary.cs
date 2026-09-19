namespace JoinCode.Reasoning.Engine;

/// <summary>
/// 推理引擎状态摘要
/// </summary>
public sealed class ReasoningSummary {
    /// <summary>
    /// 假设总数
    /// </summary>
    public int TotalAssumptions { get; init; }

    /// <summary>
    /// 已验证数量
    /// </summary>
    public int TotalVerified { get; init; }

    /// <summary>
    /// 事实总数
    /// </summary>
    public int TotalFacts { get; init; }

    /// <summary>
    /// 已拒绝数量
    /// </summary>
    public int TotalRejected { get; init; }

    /// <summary>
    /// 待证据数量
    /// </summary>
    public int TotalPendingEvidence { get; init; }

    /// <summary>
    /// 证据总数
    /// </summary>
    public int TotalEvidence { get; init; }

    /// <summary>
    /// 最近一次运行时间；未运行过则为 null
    /// </summary>
    public DateTime? LastRunAt { get; init; }

    /// <summary>
    /// 当前预算状态
    /// </summary>
    public BudgetStatus Budget { get; init; } = new() {
        RoundsUsed = 0,
        RoundsBudget = 0,
        TokensUsed = 0,
        TokensBudget = 0,
    };
}