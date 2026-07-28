namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 计划审批请求消息 — 对齐 TS PlanApprovalRequestMessageSchema
/// Teammate 退出 PlanMode 时发送给 team-lead 的审批请求
/// </summary>
public sealed class PlanApprovalRequestMessage
{
    /// <summary>
    /// 消息类型标识
    /// </summary>
    public string Type { get; init; } = TeammateMessageTypeConstants.PlanApprovalRequest;

    /// <summary>
    /// 请求来源 agent 名称
    /// </summary>
    public required string From { get; init; }

    /// <summary>
    /// 请求时间戳
    /// </summary>
    public required string Timestamp { get; init; }

    /// <summary>
    /// Plan 文件路径
    /// </summary>
    public required string PlanFilePath { get; init; }

    /// <summary>
    /// Plan 文件内容
    /// </summary>
    public required string PlanContent { get; init; }

    /// <summary>
    /// 请求 ID — 用于关联审批响应
    /// </summary>
    public required string RequestId { get; init; }
}

/// <summary>
/// 计划审批响应消息 — 对齐 TS PlanApprovalResponseMessageSchema
/// Team-lead 审批后发送给 teammate 的响应
/// </summary>
public sealed class PlanApprovalResponseMessage
{
    /// <summary>
    /// 消息类型标识
    /// </summary>
    public string Type { get; init; } = TeammateMessageTypeConstants.PlanApprovalResponse;

    /// <summary>
    /// 响应来源（team-lead）
    /// </summary>
    public required string From { get; init; }

    /// <summary>
    /// 响应时间戳
    /// </summary>
    public required string Timestamp { get; init; }

    /// <summary>
    /// 关联的请求 ID
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// 是否批准
    /// </summary>
    public required bool Approved { get; init; }

    /// <summary>
    /// 审批反馈（拒绝时说明原因）
    /// </summary>
    public string? Feedback { get; init; }

    /// <summary>
    /// Leader 授权的权限模式 — 对齐 TS permissionMode
    /// Teammate 退出 PlanMode 后应切换到此模式
    /// </summary>
    public string? PermissionMode { get; init; }
}
