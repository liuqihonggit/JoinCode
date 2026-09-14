namespace JoinCode.Abstractions.Models.Build;

/// <summary>
/// 编译队列条目状态
/// </summary>
public enum BuildQueueEntryStatus
{
    /// <summary>排队中</summary>
    [EnumValue("queued")] Queued,

    /// <summary>编译中</summary>
    [EnumValue("building")] Building,

    /// <summary>正在取消（杀进程中）</summary>
    [EnumValue("cancelling")] Cancelling,

    /// <summary>编译完成</summary>
    [EnumValue("completed")] Completed,

    /// <summary>编译失败</summary>
    [EnumValue("failed")] Failed,

    /// <summary>已取消</summary>
    [EnumValue("cancelled")] Cancelled
}
