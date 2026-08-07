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
        string? displayName = null,
        ObjectId sessionId = default)
        : base("sleep", toolUseId, spanId, displayName ?? $"sleep:{durationSeconds}s", sessionId)
    {
        DurationSeconds = durationSeconds;
        RemainingSeconds = durationSeconds;
        Reason = reason;
    }

    /// <summary>
    /// 跨会话深拷贝 — 保留 DurationSeconds/RemainingSeconds/TickCount/Reason 等延迟执行特有字段
    /// </summary>
    public override Entity Clone(CloneContext context)
    {
        var cloned = new SleepEntity(
            durationSeconds: DurationSeconds,
            reason: Reason,
            toolUseId: ToolUseId,
            spanId: SpanId,
            displayName: DisplayName,
            sessionId: context.TargetSessionId)
        {
            ArgumentsSummary = ArgumentsSummary,
            ResultSummary = ResultSummary,
            IsError = IsError,
            SessionObjectId = context.RemapNullable(SessionObjectId),
            LifecycleState = LifecycleState,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            LastActivityAt = LastActivityAt,
            RemainingSeconds = RemainingSeconds,
            TickCount = TickCount,
        };
        context.Map(ObjectId, cloned.ObjectId);
        return cloned;
    }
}
