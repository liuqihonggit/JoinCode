namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Sleep 实体 — 派生自 ToolExecutionEntity，追踪延迟执行生命周期
/// 额外字段: DurationSeconds, RemainingSeconds, TickCount, Reason
/// </summary>
public sealed class SleepEntity : ToolExecutionEntity {
    /// <summary>获取睡眠总时长（秒）。</summary>
    public int DurationSeconds { get; init; }
    /// <summary>获取或设置剩余秒数。</summary>
    public int RemainingSeconds { get; set; }
    /// <summary>获取或设置心跳计数。</summary>
    public int TickCount { get; set; }
    /// <summary>获取睡眠原因。</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// 构造睡眠实体。
    /// </summary>
    /// <param name="durationSeconds">睡眠总时长（秒）。</param>
    /// <param name="reason">睡眠原因。</param>
    /// <param name="toolUseId">工具使用 ID。</param>
    /// <param name="spanId">跨度 ID。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话 ID。</param>
    public SleepEntity(
        int durationSeconds = 0,
        string? reason = null,
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(SystemToolName.Sleep.ToValue(), toolUseId, spanId, displayName ?? $"sleep:{durationSeconds}s", sessionId) {
        DurationSeconds = durationSeconds;
        RemainingSeconds = durationSeconds;
        Reason = reason;
    }

    /// <summary>
    /// 跨会话深拷贝 — 保留 DurationSeconds/RemainingSeconds/TickCount/Reason 等延迟执行特有字段
    /// </summary>
    public override Entity Clone(CloneContext context) {
        var cloned = new SleepEntity(
            durationSeconds: DurationSeconds,
            reason: Reason,
            toolUseId: ToolUseId,
            spanId: SpanId,
            displayName: DisplayName,
            sessionId: context.TargetSessionId) {
            RemainingSeconds = RemainingSeconds,
            TickCount = TickCount,
        };
        ApplyCloneState(cloned, context);
        return cloned;
    }
}
