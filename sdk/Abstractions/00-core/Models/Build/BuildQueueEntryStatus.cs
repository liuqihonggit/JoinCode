namespace JoinCode.Abstractions.Models.Build;

/// <summary>
/// 编译队列条目状态
/// </summary>
public enum BuildQueueEntryStatus
{
    /// <summary>排队中</summary>
    Queued,

    /// <summary>编译中</summary>
    Building,

    /// <summary>正在取消（杀进程中）</summary>
    Cancelling,

    /// <summary>编译完成</summary>
    Completed,

    /// <summary>编译失败</summary>
    Failed,

    /// <summary>已取消</summary>
    Cancelled
}
