namespace JoinCode.Abstractions.Security;

/// <summary>
/// 路径权限检查结果 — 对齐 TS checkReadPermissionForTool 返回的 PermissionDecision
/// </summary>
public sealed class PathPermissionCheckResult
{
    /// <summary>
    /// 权限决策
    /// </summary>
    public PermissionBehavior Decision { get; }

    /// <summary>
    /// 决策原因
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// 匹配的规则（如果有）
    /// </summary>
    public PathPermissionRule? MatchedRule { get; }

    private PathPermissionCheckResult(PermissionBehavior decision, string? reason, PathPermissionRule? matchedRule)
    {
        Decision = decision;
        Reason = reason;
        MatchedRule = matchedRule;
    }

    /// <summary>
    /// 创建允许结果
    /// </summary>
    public static PathPermissionCheckResult Allow(string reason) =>
        new(PermissionBehavior.Allow, reason, null);

    /// <summary>
    /// 创建允许结果（携带匹配规则）
    /// </summary>
    public static PathPermissionCheckResult Allow(string reason, PathPermissionRule? rule) =>
        new(PermissionBehavior.Allow, reason, rule);

    /// <summary>
    /// 创建拒绝结果
    /// </summary>
    public static PathPermissionCheckResult Deny(string reason, PathPermissionRule? rule = null) =>
        new(PermissionBehavior.Deny, reason, rule);

    /// <summary>
    /// 创建需要确认结果
    /// </summary>
    public static PathPermissionCheckResult Ask(string reason, PathPermissionRule? rule = null) =>
        new(PermissionBehavior.Ask, reason, rule);
}
