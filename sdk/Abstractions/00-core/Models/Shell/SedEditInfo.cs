namespace JoinCode.Abstractions.Models.Shell;

/// <summary>
/// sed 编辑信息 — 对齐 TS SedEditInfo
/// 将 sed -i 命令解析为结构化的文件编辑操作
/// </summary>
public sealed record SedEditInfo
{
    /// <summary>
    /// 被编辑的文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 搜索模式（正则表达式）
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// 替换文本
    /// </summary>
    public required string Replacement { get; init; }

    /// <summary>
    /// 替换标志（g, i, m 等）
    /// </summary>
    public required string Flags { get; init; }

    /// <summary>
    /// 是否使用扩展正则（-E 或 -r 标志）
    /// </summary>
    public required bool ExtendedRegex { get; init; }
}

/// <summary>
/// sed 验证结果 — 对齐 TS sedValidation
/// </summary>
public sealed record SedValidationResult
{
    public required SedValidationBehavior Behavior { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// sed 验证行为
/// </summary>
public enum SedValidationBehavior
{
    /// <summary>
    /// 允许通过
    /// </summary>
    Passthrough,

    /// <summary>
    /// 需要用户确认
    /// </summary>
    Ask,

    /// <summary>
    /// 拒绝执行
    /// </summary>
    Deny
}
