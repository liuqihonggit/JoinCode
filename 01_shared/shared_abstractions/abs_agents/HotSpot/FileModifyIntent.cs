namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 文件修改意图记录 — Worker 上报的"我要改这个文件，意图是内部/契约"
/// 不可变 record，天然线程安全，用于 IntentCollector 收集和 HotSpotTracker 统计
/// </summary>
public sealed record FileModifyIntent
{
    /// <summary>
    /// 文件路径（相对或绝对，归一化为正斜杠）
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 修改意图：InternalChange（内部改，不触发热点）/ ContractChange（契约改，触发热点）
    /// </summary>
    public required ModifyIntent Intent { get; init; }

    /// <summary>
    /// 上报的 Worker ID（队长不计入认领集合）
    /// </summary>
    public required string WorkerId { get; init; }

    /// <summary>
    /// 上报时间（UTC）
    /// </summary>
    public required DateTimeOffset ReportedAt { get; init; }

    /// <summary>
    /// 冲突标记分类 — subAgent 上报时自带, [Flags] 可组合
    /// None=未分类, HotFileConflict=热文件冲突(队长统计>=3 升级), TestFileConflict=测试文件冲突, ResourceRefChange=资源引用变更
    /// </summary>
    public MailMarker Marker { get; init; } = MailMarker.None;

    /// <summary>
    /// 是否为契约修改（便捷判断）
    /// </summary>
    public bool IsContractChange => Intent == ModifyIntent.ContractChange;

    /// <summary>
    /// 是否为队长上报（队长不计入热点认领）
    /// </summary>
    public bool IsFromCaptain => WorkerId.Equals("captain", StringComparison.OrdinalIgnoreCase);
}
