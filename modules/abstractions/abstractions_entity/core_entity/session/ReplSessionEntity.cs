namespace JoinCode.Abstractions.Entity;

/// <summary>
/// REPL 会话实体 — 派生自 ToolExecutionEntity，追踪交互式代码执行生命周期
/// 额外字段: Language, IsEnabled
/// </summary>
public sealed class ReplSessionEntity : ToolExecutionEntity {
    /// <summary>获取 REPL 语言。</summary>
    public string Language { get; init; } = ReplLanguage.CSharp.ToValue();
    /// <summary>获取或设置是否启用。</summary>
    public bool IsEnabled { get; set; }

    /// <summary>构造 ReplSessionEntity 实例。</summary>
    public ReplSessionEntity(
        string language = ReplLanguageEnumConstants.CSharp,
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(SystemToolName.Repl.ToValue(), toolUseId, spanId, displayName ?? $"repl:{language}", sessionId) {
        Language = language;
    }

    /// <summary>
    /// 跨会话深拷贝 — 保留 Language/IsEnabled 等 REPL 会话特有字段
    /// </summary>
    public override Entity Clone(CloneContext context) {
        var cloned = new ReplSessionEntity(
            language: Language,
            toolUseId: ToolUseId,
            spanId: SpanId,
            displayName: DisplayName,
            sessionId: context.TargetSessionId) {
            IsEnabled = IsEnabled,
        };
        ApplyCloneState(cloned, context);
        return cloned;
    }
}