namespace Core.Hooks.ToolPermission;

/// <summary>
/// 权限决策来源类型
/// </summary>
public enum PermissionDecisionSourceType
{
    /// <summary>通过 Hook 自动批准</summary>
    [EnumValue("hook")] Hook,

    /// <summary>用户批准</summary>
    [EnumValue("user")] User,

    /// <summary>分类器自动批准</summary>
    [EnumValue("classifier")] Classifier,

    /// <summary>配置自动批准</summary>
    [EnumValue("config")] Config,

    /// <summary>用户中止</summary>
    [EnumValue("userAbort")] UserAbort,

    /// <summary>用户拒绝</summary>
    [EnumValue("userReject")] UserReject,

    /// <summary>未知来源</summary>
    [EnumValue("unknown")] Unknown
}

/// <summary>
/// 权限批准来源
/// </summary>
public sealed record PermissionApprovalSource
{
    /// <summary>
    /// 决策来源类型
    /// </summary>
    public required PermissionDecisionSourceType Type { get; init; }

    /// <summary>
    /// 是否为永久批准（仅适用于用户类型）
    /// </summary>
    public bool Permanent { get; init; }

    /// <summary>
    /// Hook 名称（仅适用于 Hook 类型）
    /// </summary>
    public string? HookName { get; init; }
}

/// <summary>
/// 权限拒绝来源
/// </summary>
public sealed record PermissionRejectionSource
{
    /// <summary>
    /// 决策来源类型
    /// </summary>
    public required PermissionDecisionSourceType Type { get; init; }

    /// <summary>
    /// 是否有反馈（仅适用于用户拒绝类型）
    /// </summary>
    public bool HasFeedback { get; init; }

    /// <summary>
    /// Hook 名称（仅适用于 Hook 类型）
    /// </summary>
    public string? HookName { get; init; }

    /// <summary>
    /// 拒绝原因
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 分类器决策原因
/// </summary>
public sealed record ClassifierDecisionReason
{
    /// <summary>
    /// 决策类型标识
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// 分类器类型
    /// </summary>
    public string? Classifier { get; init; }

    /// <summary>
    /// 决策原因描述
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 权限决策原因
/// </summary>
public abstract record PermissionDecisionReason
{
    /// <summary>
    /// 决策类型标识
    /// </summary>
    public abstract string Type { get; }
}

/// <summary>
/// Hook 决策原因
/// </summary>
public sealed record HookDecisionReason : PermissionDecisionReason
{
    /// <summary>
    /// 决策类型标识 — 固定为 "hook"
    /// </summary>
    public override string Type => "hook";

    /// <summary>
    /// Hook 名称
    /// </summary>
    public required string HookName { get; init; }

    /// <summary>
    /// 决策原因描述
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 分类器决策原因
/// </summary>
public sealed record ClassifierPermissionDecisionReason : PermissionDecisionReason
{
    /// <summary>
    /// 决策类型标识 — 固定为 "classifier"
    /// </summary>
    public override string Type => "classifier";

    /// <summary>
    /// 分类器类型
    /// </summary>
    public required string Classifier { get; init; }

    /// <summary>
    /// 决策原因描述
    /// </summary>
    public string? Reason { get; init; }
}
