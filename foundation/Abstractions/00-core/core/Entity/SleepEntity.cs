namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Sleep 实体 — 派生自 ToolExecutionEntity，追踪延迟执行生命周期
/// 额外字段: DurationSeconds, RemainingSeconds, TickCount, Reason
/// </summary>
public sealed class SleepEntity : ToolExecutionEntity
{
    public int DurationSeconds { get; init; }
    public int RemainingSeconds { get; set; }
    public int TickCount { get; set; }
    public string? Reason { get; init; }

    public SleepEntity(
        int durationSeconds = 0,
        string? reason = null,
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null)
        : base("sleep", toolUseId, spanId, displayName ?? $"sleep:{durationSeconds}s")
    {
        DurationSeconds = durationSeconds;
        RemainingSeconds = durationSeconds;
        Reason = reason;
    }
}
