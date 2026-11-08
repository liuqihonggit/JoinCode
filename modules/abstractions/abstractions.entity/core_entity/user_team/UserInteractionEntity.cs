namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 用户交互实体 — 派生自 ToolExecutionEntity，追踪等待用户输入的生命周期
/// 额外字段: Question, Response
/// </summary>
public sealed class UserInteractionEntity : ToolExecutionEntity {
    /// <summary>获取提问内容。</summary>
    public string? Question { get; init; }
    /// <summary>获取或设置用户回答。</summary>
    public string? Response { get; set; }

    /// <summary>构造用户交互实体。</summary>
    /// <param name="question">提问内容。</param>
    /// <param name="toolUseId">工具使用标识。</param>
    /// <param name="spanId">跨度标识。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话标识。</param>
    public UserInteractionEntity(
        string? question = null,
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base("ask_user", toolUseId, spanId, displayName ?? "ask_user", sessionId) {
        Question = question;
    }

    /// <summary>
    /// 跨会话深拷贝 — 保留 Question/Response 等用户交互特有字段
    /// </summary>
    public override Entity Clone(CloneContext context) {
        var cloned = new UserInteractionEntity(
            question: Question,
            toolUseId: ToolUseId,
            spanId: SpanId,
            displayName: DisplayName,
            sessionId: context.TargetSessionId) {
            Response = Response,
        };
        ApplyCloneState(cloned, context);
        return cloned;
    }
}