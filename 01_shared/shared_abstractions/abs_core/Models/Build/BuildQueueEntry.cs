namespace JoinCode.Abstractions.Models.Build;

/// <summary>
/// 编译队列条目
/// </summary>
public sealed class BuildQueueEntry
{
    /// <summary>
    /// 编译 ID
    /// </summary>
    public required string BuildId { get; init; }

    /// <summary>
    /// 编译请求
    /// </summary>
    public required BuildRequest Request { get; init; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public BuildQueueEntryStatus Status { get; set; } = BuildQueueEntryStatus.Queued;

    /// <summary>
    /// 编译结果（完成后设置）
    /// </summary>
    public BuildQueueResult? Result { get; set; }

    /// <summary>
    /// 开始编译时间
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// 编译完成时间
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// 排队位置
    /// </summary>
    public int QueuePosition { get; set; }
}
