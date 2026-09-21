namespace JoinCode.Abstractions.Models.Shell;

/// <summary>
/// sed 编辑信息 — 对齐 TS SedEditInfo
/// 将 sed -i 命令解析为结构化的文件编辑操作
/// </summary>
public sealed record SedEditInfo {
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
public sealed record SedValidationResult : ShellPermissionCheckResult {
    /// <summary>构造默认 sed 验证结果。</summary>
    public SedValidationResult() : base(PermissionBehavior.Passthrough) { }

    /// <summary>
    /// 构造 sed 验证结果。
    /// </summary>
    /// <param name="behavior">权限行为。</param>
    /// <param name="message">消息。</param>
    public SedValidationResult(PermissionBehavior behavior, string? message = null) : base(behavior, message) { }
}
