namespace JoinCode.Abstractions.Security;

/// <summary>
/// 权限待确认异常，当工具调用需要用户确认时抛出
/// </summary>
public sealed partial class PermissionPendingConfirmationException : WorkflowException
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// 确认提示信息
    /// </summary>
    public string ConfirmationPrompt { get; }

    /// <summary>
    /// 请求ID
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// 规则内容 — 对齐 TS 版 ruleContent
    /// WebFetch 使用 "domain:example.com" 格式，用于域名级白名单持久化
    /// </summary>
    public string? RuleContent { get; }

    /// <summary>
    /// 创建权限待确认异常
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="confirmationPrompt">确认提示</param>
    /// <param name="requestId">请求ID</param>
    /// <param name="ruleContent">规则内容（如 domain:example.com）</param>
    public PermissionPendingConfirmationException(string toolName, string confirmationPrompt, string? requestId = null, string? ruleContent = null)
        : base(
            $"工具 '{toolName}' 需要确认: {confirmationPrompt}",
            "PERM_CONFIRM",
            ErrorCategory.Permission)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        ConfirmationPrompt = confirmationPrompt ?? throw new ArgumentNullException(nameof(confirmationPrompt));
        RequestId = requestId;
        RuleContent = ruleContent;
    }
}
