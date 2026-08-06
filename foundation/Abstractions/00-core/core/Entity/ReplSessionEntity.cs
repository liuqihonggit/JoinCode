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
}
