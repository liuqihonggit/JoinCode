namespace JoinCode.Abstractions.Planning;

/// <summary>
/// 退出 PlanMode 时允许的语义级 Bash 权限
/// 对齐 TS: ExitPlanModeV2Tool.ts — AllowedPrompt
/// { tool: 'Bash', prompt: string }
/// </summary>
public sealed record AllowedPrompt
{
    /// <summary>
    /// 工具名称（当前仅支持 Bash）
    /// 对齐 TS: z.enum(['Bash'])
    /// </summary>
    public string Tool { get; init; } = AllowedPromptToolConstants.Bash;

    /// <summary>
    /// 语义描述，如 "run tests"、"install dependencies"
    /// 对齐 TS: prompt 字段
    /// </summary>
    public string Prompt { get; init; } = string.Empty;
}

/// <summary>
/// AllowedPrompt.Tool 允许的工具名常量
/// 对齐 TS: z.enum(['Bash'])
/// </summary>
public enum AllowedPromptTool
{
    [EnumValue("Bash")] Bash,
}
