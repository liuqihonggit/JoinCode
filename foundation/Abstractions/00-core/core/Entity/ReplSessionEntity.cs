namespace JoinCode.Abstractions.Entity;

/// <summary>
/// REPL 会话实体 — 派生自 ToolExecutionEntity，追踪交互式代码执行生命周期
/// 额外字段: Language, IsEnabled
/// </summary>
public sealed class ReplSessionEntity : ToolExecutionEntity
{
    public string Language { get; init; } = "csharp";
    public bool IsEnabled { get; set; }

    public ReplSessionEntity(
        string language = "csharp",
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base("repl", toolUseId, spanId, displayName ?? $"repl:{language}", sessionId)
    {
        Language = language;
    }

    /// <summary>
    /// 跨会话深拷贝 — 保留 Language/IsEnabled 等 REPL 会话特有字段
    /// </summary>
    public override Entity Clone(CloneContext context)
    {
        var cloned = new ReplSessionEntity(
            language: Language,
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
            IsEnabled = IsEnabled,
        };
        context.Map(ObjectId, cloned.ObjectId);
        return cloned;
    }
}
