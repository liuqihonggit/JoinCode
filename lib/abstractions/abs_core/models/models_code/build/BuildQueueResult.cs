namespace JoinCode.Abstractions.Models.Build;

/// <summary>
/// 编译队列结果
/// </summary>
public sealed record BuildQueueResult
{
    /// <summary>
    /// 编译 ID
    /// </summary>
    public required string BuildId { get; init; }

    /// <summary>
    /// 退出码
    /// </summary>
    public required int ExitCode { get; init; }

    /// <summary>
    /// 标准输出
    /// </summary>
    public required string Output { get; init; }

    /// <summary>
    /// 错误输出
    /// </summary>
    public required string ErrorOutput { get; init; }

    /// <summary>
    /// 队列等待时长
    /// </summary>
    public required TimeSpan WaitDuration { get; init; }

    /// <summary>
    /// 编译执行时长
    /// </summary>
    public required TimeSpan BuildDuration { get; init; }

    /// <summary>
    /// 排队位置
    /// </summary>
    public required int QueuePosition { get; init; }

    /// <summary>
    /// 是否检测到系统睡眠
    /// </summary>
    public bool SleepDetected { get; init; }

    /// <summary>
    /// 是否被取消
    /// </summary>
    public bool Cancelled { get; init; }
}
