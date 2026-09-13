namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 权限检查结果 — CheckPermissionAsync 的返回值,替代异常传播
/// </summary>
public sealed record PermissionCheckOutcome
{
    /// <summary>权限决策</summary>
    public required PermissionDecision Decision { get; init; }

    /// <summary>拒绝原因 — Decision 为 Denied 时填充</summary>
    public string? DenyReason { get; init; }

    /// <summary>确认提示 — Decision 为 PendingConfirmation 时填充</summary>
    public string? ConfirmationPrompt { get; init; }

    /// <summary>规则内容 — WebFetch 等 domain:hostname 格式</summary>
    public string? RuleContent { get; init; }

    /// <summary>允许执行的预设结果</summary>
    public static readonly PermissionCheckOutcome Allowed = new() { Decision = PermissionDecision.Allowed };

    /// <summary>创建拒绝结果</summary>
    public static PermissionCheckOutcome Denied(string reason) => new() { Decision = PermissionDecision.Denied, DenyReason = reason };

    /// <summary>创建待确认结果</summary>
    public static PermissionCheckOutcome Pending(string prompt, string? ruleContent = null) =>
        new() { Decision = PermissionDecision.PendingConfirmation, ConfirmationPrompt = prompt, RuleContent = ruleContent };
}
