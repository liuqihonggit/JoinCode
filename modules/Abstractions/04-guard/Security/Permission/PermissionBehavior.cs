namespace JoinCode.Abstractions.Security;

/// <summary>
/// 权限决策行为枚举 — 统一权限检查/Hook决策/Shell安全的决策行为
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 PermissionBehaviorConstants + PermissionBehaviorExtensions
/// 合并自: HookDecisionType (Block), ShellSecurityBehavior (Passthrough)
/// </summary>
public enum PermissionBehavior
{
    /// <summary>允许执行</summary>
    [EnumValue("allow")] Allow = 0,

    /// <summary>拒绝执行</summary>
    [EnumValue("deny")] Deny = 1,

    /// <summary>需要用户确认</summary>
    [EnumValue("ask")] Ask = 2,

    /// <summary>阻止执行（Hook 层决策，语义同 Deny）</summary>
    [EnumValue("block")] Block = 3,

    /// <summary>无规则匹配，传递给上层（Shell 安全链决策）</summary>
    [EnumValue("passthrough")] Passthrough = 4
}
