namespace JoinCode.Abstractions.Models.Build;

/// <summary>
/// 编译队列状态
/// </summary>
public sealed record BuildQueueStatus
{
    /// <summary>
    /// 排队中数量
    /// </summary>
    public required int PendingCount { get; init; }

    /// <summary>
    /// 是否正在编译
    /// </summary>
    public required bool IsBuilding { get; init; }

    /// <summary>
    /// 当前编译 ID
    /// </summary>
    public string? CurrentBuildId { get; init; }

    /// <summary>
    /// 当前编译的 Agent ID
    /// </summary>
    public string? CurrentBuildAgentId { get; init; }

    /// <summary>
    /// 最近的编译记录
    /// </summary>
    public IReadOnlyList<BuildQueueEntry> RecentBuilds { get; init; } = [];
}
