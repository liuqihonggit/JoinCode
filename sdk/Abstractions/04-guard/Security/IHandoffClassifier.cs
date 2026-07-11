namespace JoinCode.Abstractions.Security;

/// <summary>
/// 子智能体交接安全审查 — 对齐 TS classifyHandoffIfNeeded
/// </summary>
public interface IHandoffClassifier
{
    /// <summary>
    /// 分类子智能体交接是否安全
    /// </summary>
    Task<HandoffClassificationResult?> ClassifyAsync(HandoffClassificationRequest request, CancellationToken ct = default);
}

/// <summary>
/// 交接分类请求 — 对齐 TS classifyHandoffIfNeeded 参数
/// </summary>
public sealed partial class HandoffClassificationRequest
{
    /// <summary>
    /// 子智能体 ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// 子智能体类型（如 Explore/Plan/generic）
    /// </summary>
    public string? AgentType { get; init; }

    /// <summary>
    /// 子智能体执行的工具调用记录
    /// </summary>
    public required IReadOnlyList<AgentToolInvocation> ToolInvocations { get; init; }

    /// <summary>
    /// 当前权限模式 — 仅 auto 模式下需要审查
    /// </summary>
    public required PermissionMode PermissionMode { get; init; }

    /// <summary>
    /// 工具调用总数
    /// </summary>
    public int TotalToolUseCount { get; init; }
}

/// <summary>
/// 子智能体工具调用记录
/// </summary>
public sealed partial class AgentToolInvocation
{
    public required string ToolName { get; init; }
    public required OperationType OperationType { get; init; }
    public Dictionary<string, JsonElement>? Parameters { get; init; }
    public bool WasAutoApproved { get; init; }
}

/// <summary>
/// 交接分类结果 — 对齐 TS classifyHandoffIfNeeded 返回值
/// </summary>
public sealed partial class HandoffClassificationResult
{
    /// <summary>
    /// 分类级别
    /// </summary>
    public required HandoffClassification Classification { get; init; }

    /// <summary>
    /// 警告消息 — 对齐 TS 返回的警告字符串
    /// </summary>
    public string? WarningMessage { get; init; }

    /// <summary>
    /// 分类原因
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 交接分类级别 — 对齐 TS classifyHandoffIfNeeded 的三种结果
/// </summary>
public enum HandoffClassification
{
    /// <summary>
    /// 安全 — 无需警告（对应 TS 返回 null）
    /// </summary>
    [EnumValue("allowed")] Allowed,

    /// <summary>
    /// 分类器不可用 — 温和提示（对应 TS unavailable）
    /// </summary>
    [EnumValue("unavailable")] Unavailable,

    /// <summary>
    /// 被拦截 — 安全警告（对应 TS blocked）
    /// </summary>
    [EnumValue("blocked")] Blocked
}
