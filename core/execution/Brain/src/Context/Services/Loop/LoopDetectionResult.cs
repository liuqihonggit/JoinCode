namespace Core.Context;

public sealed record LoopDetectionResult(
    bool IsLoopDetected,
    string? RepeatedPattern,
    int RepeatCount,
    int LoopStartIndex,
    int LoopTriggerCount = 0)
{
    public static readonly LoopDetectionResult NoLoop = new(false, null, 0, 0);
}
