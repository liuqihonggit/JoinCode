namespace JoinCode.Abstractions.Models.Agent;

public enum TeammateMessageType
{
    [EnumValue("idle_notification")] IdleNotification,
    [EnumValue("shutdown_request")] ShutdownRequest,
    [EnumValue("shutdown_approved")] ShutdownApproved,
    [EnumValue("shutdown_rejected")] ShutdownRejected,
    [EnumValue("new_message")] NewMessage,
    [EnumValue("task_assignment")] TaskAssignment,
    [EnumValue("permission_request")] PermissionRequest,
    [EnumValue("permission_response")] PermissionResponse,
    [EnumValue("sandbox_permission_request")] SandboxPermissionRequest,
    [EnumValue("sandbox_permission_response")] SandboxPermissionResponse,
    [EnumValue("team_permission_update")] TeamPermissionUpdate,
    [EnumValue("mode_set_request")] ModeSetRequest,
    [EnumValue("plan_approval_request")] PlanApprovalRequest,
    [EnumValue("plan_approval_response")] PlanApprovalResponse
}

/// <summary>
/// 队友等待结果
/// </summary>
public enum TeammateWaitResult
{
    [EnumValue("shutdown_request")] ShutdownRequest,
    [EnumValue("new_message")] NewMessage,
    [EnumValue("aborted")] Aborted
}

public sealed class TeammateStructuredMessage
{
    public required TeammateMessageType Type { get; init; }
    public required string RequestId { get; init; }
    public string? FromAgentId { get; init; }
    public string? ToAgentId { get; init; }
    public string? Reason { get; init; }
    public string? Content { get; init; }
    public Dictionary<string, JsonElement>? Payload { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed class TeammateIdleNotification
{
    public required string AgentId { get; init; }
    public required string TeamName { get; init; }
    public string? TeamId { get; init; }
    public string? LastResult { get; init; }
    public DateTime IdledAt { get; init; } = DateTime.UtcNow;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed class TeammateShutdownRequest
{
    public required string RequestId { get; init; }
    public required string AgentId { get; init; }
    public string? Reason { get; init; }
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
}
