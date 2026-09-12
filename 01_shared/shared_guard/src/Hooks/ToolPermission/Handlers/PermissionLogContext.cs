namespace Core.Hooks.ToolPermission;

/// <summary>
/// 权限日志上下文
/// </summary>
public sealed record PermissionLogContext
{
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
public abstract record PermissionDecisionArgs
{
    public abstract string Decision { get; }
    public abstract PermissionDecisionSourceType Source { get; }
}

/// <summary>
/// 批准决策参数
/// </summary>
public sealed record AcceptDecisionArgs : PermissionDecisionArgs
{
    public override string Decision => "accept";
    public override PermissionDecisionSourceType Source => ApprovalSource.Type;
    public required PermissionApprovalSource ApprovalSource { get; init; }
}

/// <summary>
/// 拒绝决策参数
/// </summary>
public sealed record RejectDecisionArgs : PermissionDecisionArgs
{
    public override string Decision => "reject";
    public override PermissionDecisionSourceType Source => RejectionSource.Type;
    public required PermissionRejectionSource RejectionSource { get; init; }
}

/// <summary>
/// 权限结果类型
/// </summary>
public enum PermissionResultType
{
    [EnumValue("granted")] Granted,
    [EnumValue("denied")] Denied,
    [EnumValue("pending")] Pending
}

/// <summary>
/// 权限结果
/// </summary>
public sealed record PermissionResult
{
    public required PermissionResultType Type { get; init; }
    public string? Message { get; init; }

    public static PermissionResult Granted() => new() { Type = PermissionResultType.Granted };
    public static PermissionResult Denied(string message) => new() { Type = PermissionResultType.Denied, Message = message };
    public static PermissionResult PendingConfirmation(string message) => new() { Type = PermissionResultType.Pending, Message = message };
}


