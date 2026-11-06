namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// Bash 防御上下文 — 在防御链各步骤间传递的可变状态。
/// <para>
/// 字段填充时机：
/// <list type="bullet">
/// <item><see cref="ParsedCommand"/> — StrictParse 步骤后填充</item>
/// <item><see cref="DangerLevel"/> — Classify 步骤后填充</item>
/// <item><see cref="ExecutionResult"/> — PostToolUse 步骤后填充</item>
/// </list>
/// </para>
/// </summary>
public sealed class BashDefenseContext {
    /// <summary>原始命令（调用方传入，未经任何改写）</summary>
    public required string OriginalCommand { get; init; }

    /// <summary>当前命令（可能被改写步骤更新，初始等于 OriginalCommand）</summary>
    public string CurrentCommand { get; set; } = string.Empty;

    /// <summary>工作目录路径（用于重定向白名单判定）</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Shell 类型（Bash/PowerShell/Cmd 等）</summary>
    public required SystemActuatorKind ShellKind { get; init; }

    /// <summary>确认模式（None/AntiCharLossConfirm）</summary>
    public GuardConfirmMode ConfirmMode { get; init; }

    /// <summary>已确认的命令字符串（二次确认场景，null 表示未确认）</summary>
    public string? ConfirmedCommand { get; init; }

    /// <summary>argv hash（防意图反推，由解析结果计算得出，调用方传入）</summary>
    public string? ArgvHash { get; init; }

    /// <summary>解析结果（StrictParse 步骤填充，null 表示未解析）</summary>
    public ShellCommand? ParsedCommand { get; set; }

    /// <summary>危险等级（Classify 步骤填充，默认 Unknown）</summary>
    public CommandDangerLevel DangerLevel { get; set; } = CommandDangerLevel.Unknown;

    /// <summary>执行结果（PostToolUse 步骤填充，null 表示未执行或未到 PostToolUse）</summary>
    public object? ExecutionResult { get; set; }
}