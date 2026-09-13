namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 用户交互实体 — 派生自 ToolExecutionEntity，追踪等待用户输入的生命周期
/// 额外字段: Question, Response
/// </summary>
public sealed class UserInteractionEntity : ToolExecutionEntity
{
    public string? Question { get; init; }
    public string? Response { get; set; }

    public UserInteractionEntity(
        string? question = null,
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base("ask_user", toolUseId, spanId, displayName ?? "ask_user", sessionId)
    {
        Question = question;
    }

    /// <summary>
    /// 跨会话深拷贝 — 保留 Question/Response 等用户交互特有字段
    /// </summary>
    public override Entity Clone(CloneContext context)
    {
        var cloned = new UserInteractionEntity(
            question: Question,
            toolUseId: ToolUseId,
            spanId: SpanId,
            displayName: DisplayName,
            sessionId: context.TargetSessionId)
        {
            Response = Response,
        };
        ApplyCloneState(cloned, context);
        return cloned;
    }
}
