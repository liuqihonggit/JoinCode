namespace Core.Context;

/// <summary>
/// 循环检测结果 — 包含是否检测到循环、重复模式、重复次数、起始索引和累计触发次数
/// </summary>
/// <param name="IsLoopDetected">是否检测到循环</param>
/// <param name="RepeatedPattern">检测到的重复模式文本，未检测到时为 null</param>
/// <param name="RepeatCount">重复次数</param>
/// <param name="LoopStartIndex">循环起始位置索引</param>
/// <param name="LoopTriggerCount">累计循环触发次数</param>
public sealed record LoopDetectionResult(
    bool IsLoopDetected,
    string? RepeatedPattern,
    int RepeatCount,
    int LoopStartIndex,
    int LoopTriggerCount = 0)
{
    /// <summary>
    /// 未检测到循环的空结果
    /// </summary>
    public static readonly LoopDetectionResult NoLoop = new(false, null, 0, 0);
}
