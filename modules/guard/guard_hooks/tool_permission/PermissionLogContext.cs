namespace Core.Hooks.ToolPermission;

/// <summary>
/// 权限日志上下文
/// </summary>
public sealed record PermissionLogContext {
    /// <summary>
    /// 工具名称
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// 工具输入参数
    /// </summary>
    public required Dictionary<string, JsonElement> Input { get; init; }

    /// <summary>
    /// 消息ID
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// 工具使用ID
    /// </summary>
    public required string ToolUseId { get; init; }

    /// <summary>
    /// 是否启用沙箱
    /// </summary>
    public bool SandboxEnabled { get; init; }

    /// <summary>
    /// 等待用户权限的时间（毫秒）
    /// </summary>
    public int? WaitingForUserPermissionMs { get; init; }
}

/// <summary>
/// 权限决策日志参数
/// </summary>
public abstract record PermissionDecisionArgs {
    /// <summary>
    /// 决策类型标识（accept/reject）
    /// </summary>
    public abstract string Decision { get; }

    /// <summary>
    /// 决策来源类型
    /// </summary>
    public abstract PermissionDecisionSourceType Source { get; }
}

/// <summary>
/// 批准决策参数
/// </summary>
public sealed record AcceptDecisionArgs : PermissionDecisionArgs {
    /// <summary>
    /// 决策类型标识 — 固定为 "accept"
    /// </summary>
    public override string Decision => "accept";

    /// <summary>
    /// 决策来源类型 — 取自批准来源的类型
    /// </summary>
    public override PermissionDecisionSourceType Source => ApprovalSource.Type;

    /// <summary>
    /// 批准来源详情
    /// </summary>
    public required PermissionApprovalSource ApprovalSource { get; init; }
}

/// <summary>
/// 拒绝决策参数
/// </summary>
public sealed record RejectDecisionArgs : PermissionDecisionArgs {
    /// <summary>
    /// 决策类型标识 — 固定为 "reject"
    /// </summary>
    public override string Decision => "reject";

    /// <summary>
    /// 决策来源类型 — 取自拒绝来源的类型
    /// </summary>
    public override PermissionDecisionSourceType Source => RejectionSource.Type;

    /// <summary>
    /// 拒绝来源详情
    /// </summary>
    public required PermissionRejectionSource RejectionSource { get; init; }
}

/// <summary>
/// 权限结果类型
/// </summary>
public enum PermissionResultType {
    /// <summary>已批准</summary>
    [EnumValue("granted")] Granted,

    /// <summary>已拒绝</summary>
    [EnumValue("denied")] Denied,

    /// <summary>待确认</summary>
    [EnumValue("pending")] Pending
}

/// <summary>
/// 权限结果
/// </summary>
public sealed record PermissionResult {
    /// <summary>
    /// 权限结果类型
    /// </summary>
    public required PermissionResultType Type { get; init; }

    /// <summary>
    /// 附加消息（如拒绝原因）
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 创建已批准结果
    /// </summary>
    public static PermissionResult Granted() => new() { Type = PermissionResultType.Granted };

    /// <summary>
    /// 创建已拒绝结果
    /// </summary>
    public static PermissionResult Denied(string message) => new() { Type = PermissionResultType.Denied, Message = message };

    /// <summary>
    /// 创建待确认结果
    /// </summary>
    public static PermissionResult PendingConfirmation(string message) => new() { Type = PermissionResultType.Pending, Message = message };
}