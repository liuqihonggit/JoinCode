namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 热点信息 — 某文件的修改认领统计和热点判定结果
/// 不可变 record，由 HotSpotTracker 生成
/// </summary>
public sealed record HotSpotInfo
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 契约修改认领数（不含队长，队长修改不计入认领集合）
    /// </summary>
    public required int ContractClaimCount { get; init; }

    /// <summary>
    /// 内部修改认领数（不含队长）
    /// </summary>
    public required int InternalClaimCount { get; init; }

    /// <summary>
    /// 是否热文件（由 IHotFileDetector 判断）
    /// </summary>
    public required bool IsHotFile { get; init; }

    /// <summary>
    /// 是否触发热点（热文件 contract_claim>=1 即触发；非热文件 contract_claim>=阈值才触发）
    /// </summary>
    public required bool IsHotSpot { get; init; }

    /// <summary>
    /// 认领该文件契约修改的 Worker 列表（不含队长）
    /// </summary>
    public required IReadOnlyList<string> ClaimingWorkers { get; init; }
}
