namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 结构化消息解析结果 — 对齐 TS SendMessageTool 的 discriminatedUnion 输入
/// </summary>
public sealed class StructuredMessageData
{
    /// <summary>
    /// 消息类型
    /// </summary>
    public required TeammateMessageType Type { get; init; }

    /// <summary>
    /// 请求ID（shutdown_response/plan_approval_response 需要）
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// 是否批准（shutdown_response/plan_approval_response）
    /// </summary>
    public bool? Approve { get; init; }

    /// <summary>
    /// 原因（shutdown_request/shutdown_rejected）
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// 反馈（plan_approval_response 拒绝时）
    /// </summary>
    public string? Feedback { get; init; }

    /// <summary>
    /// 消息内容
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// 原始 JSON 数据（包含所有字段）
    /// </summary>
    public Dictionary<string, JsonElement>? Payload { get; init; }

    /// <summary>
    /// 生成自动分类器输入文本 — 对齐 TS toAutoClassifierInput
    /// </summary>
    public string ToAutoClassifierInput(string recipient)
    {
        return Type switch
        {
            TeammateMessageType.ShutdownRequest => $"shutdown_request to {recipient}",
            TeammateMessageType.ShutdownApproved => $"shutdown_response approved {RequestId}",
            TeammateMessageType.ShutdownRejected => $"shutdown_response rejected {RequestId}",
            TeammateMessageType.PlanApprovalRequest => $"plan_approval_request to {recipient}",
            TeammateMessageType.PlanApprovalResponse when Approve == true => $"plan_approval approved to {recipient}",
            TeammateMessageType.PlanApprovalResponse => $"plan_approval rejected to {recipient}",
            _ => $"to {recipient}: {Content}"
        };
    }
}
