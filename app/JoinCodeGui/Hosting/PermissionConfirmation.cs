namespace JoinCode.Gui.Hosting;

/// <summary>
/// 权限确认请求 — 引擎权限待确认时由网关传给 UI 决策的数据载体
/// </summary>
public sealed record PermissionConfirmationRequest(
    string ToolName,
    string ConfirmationPrompt,
    string? RequestId,
    string? RuleContent);

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
