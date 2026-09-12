namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 守卫确认模式 — 替代 bool AntiCharLossConfirm，强类型枚举 — ADR 0012
/// </summary>
public enum GuardConfirmMode
{
    /// <summary>
    /// 无确认模式 — 默认行为
    /// </summary>
    None,

    /// <summary>
    /// 防丢字符二次确认 — MTP 加速推理时防止丢字符/乱入字符导致命令变形
    /// </summary>
    AntiCharLossConfirm
}

/// <summary>
/// 命令守卫执行上下文 — 强类型替代 IReadOnlyDictionary&lt;string, object&gt; — ADR 0012
/// <para>
/// 所有字段类型明确，无需 object 装箱/类型转换，AOT 完全友好。
/// </para>
/// </summary>
/// <param name="ShellKind">Shell 类型（Bash/PowerShell/Cmd 等）</param>
/// <param name="WorkingDirectory">工作目录路径</param>
/// <param name="ConfirmMode">确认模式（None/AntiCharLossConfirm）</param>
/// <param name="ConfirmedCommand">已确认的命令字符串（二次确认场景，null 表示未确认）</param>
/// <param name="ProxyUrl">VPN 代理 URL（VPN 激活时由调用方注入，null 表示无代理）</param>
/// <param name="PrBody">PR body 内容（gh pr create 时由调用方注入，null 表示未提供）</param>
/// <param name="PrTitle">PR 标题（生成默认 body 模板时使用，null 表示未提供）</param>
/// <param name="HeadBranch">PR 头分支名（生成默认 body 模板时使用，null 表示未提供）</param>
public sealed record GuardContext(
    SystemActuatorKind ShellKind,
    string WorkingDirectory,
    GuardConfirmMode ConfirmMode = GuardConfirmMode.None,
    string? ConfirmedCommand = null,
    string? ProxyUrl = null,
    string? PrBody = null,
    string? PrTitle = null,
    string? HeadBranch = null);
