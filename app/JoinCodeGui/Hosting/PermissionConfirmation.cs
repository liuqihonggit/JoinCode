namespace JoinCode.Gui.Hosting;

/// <summary>
/// 权限确认请求 — 引擎权限待确认时由网关传给 UI 决策的数据载体
/// </summary>
public sealed record PermissionConfirmationRequest(
    string ToolName,
    string ConfirmationPrompt,
    string? RequestId,
    string? RuleContent)
{
    /// <summary>是否有规则内容（驱动 RuleContent TextBlock 显隐）</summary>
    public bool HasRuleContent => !string.IsNullOrWhiteSpace(RuleContent);

    /// <summary>
    /// 危险等级 — 从 ConfirmationPrompt 解析的 [黄灯ask]/[绿灯ask]/[红灯ask] 标签，
    /// 驱动 PermissionDialog 颜色区分（黄/绿/红/黑灯）与震动动画触发（红灯/黑灯）。
    /// null 表示未标记等级（默认中性色，不震动）。
    /// </summary>
    public CommandDangerLevel? DangerLevel { get; init; }

    /// <summary>
    /// 是否需要震动 — 黄灯(未知命令需警觉)/红灯(不可撤回操作需警告)触发;
    /// 绿灯(可撤回)不震动;黑灯(Dangerous)直接拒绝不弹窗,不会走到此判断。
    /// </summary>
    public bool ShouldShake => DangerLevel is CommandDangerLevel.Unknown or CommandDangerLevel.Execution;
}

/// <summary>
/// 权限确认决策 — GUI/CLI 弹窗结果
/// </summary>
public enum PermissionConfirmationDecision
{
    /// <summary>拒绝本次执行</summary>
    Deny,

    /// <summary>允许本次执行（临时批准一段时间）</summary>
    Allow,

    /// <summary>始终允许该工具（较长临时批准窗口）</summary>
    AlwaysAllow
}
