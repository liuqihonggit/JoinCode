namespace JoinCode.Abstractions.Guard.Rules;

/// <summary>
/// 描述性规则基类 — 提取 OperationPattern、SensitivePathPattern、DangerousCommandPattern、ToolPermissionRule 共同的 Value + Description 模式
/// </summary>
public abstract class DescribedRule
{
    /// <summary>
    /// 规则值（模式、路径、命令等）
    /// </summary>
    [Required]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 规则描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
