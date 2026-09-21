namespace JoinCode.Abstractions.Models.Agent;

public enum TeammateMessageType {
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
    [EnumValue("plan_approval_response")] PlanApprovalResponse,
    [EnumValue("intent_report")] IntentReport,
    [EnumValue("contract_changed")] ContractChanged,
    [EnumValue("force_sync")] ForceSync,
    [EnumValue("deferred_mail")] DeferredMail
}

/// <summary>
/// 队友等待结果
/// </summary>
public enum TeammateWaitResult {
    [EnumValue("shutdown_request")] ShutdownRequest,
    [EnumValue("new_message")] NewMessage,
    [EnumValue("aborted")] Aborted
}

public sealed class TeammateStructuredMessage {
    /// <summary>获取消息类型。</summary>
    public required TeammateMessageType Type { get; init; }
    /// <summary>获取请求标识。</summary>
    public required string RequestId { get; init; }
    /// <summary>获取来源智能体标识。</summary>
    public string? FromAgentId { get; init; }
    /// <summary>获取目标智能体标识。</summary>
    public string? ToAgentId { get; init; }
    /// <summary>获取原因。</summary>
    public string? Reason { get; init; }
    /// <summary>获取内容。</summary>
    public string? Content { get; init; }
    /// <summary>获取负载数据。</summary>
    public Dictionary<string, JsonElement> Payload { get; init; } = [];
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed class TeammateIdleNotification {
    /// <summary>获取智能体标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取团队名称。</summary>
    public required string TeamName { get; init; }
    /// <summary>获取团队标识。</summary>
    public string? TeamId { get; init; }
    /// <summary>获取最后结果。</summary>
    public string? LastResult { get; init; }
    /// <summary>获取空闲时间。</summary>
    public DateTime IdledAt { get; init; } = DateTime.UtcNow;
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed class TeammateShutdownRequest {
    /// <summary>获取请求标识。</summary>
    public required string RequestId { get; init; }
    /// <summary>获取智能体标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取原因。</summary>
    public string? Reason { get; init; }
    /// <summary>获取请求时间。</summary>
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
}