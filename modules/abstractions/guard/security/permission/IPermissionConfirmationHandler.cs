namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 权限确认处理器 — 工具执行需要用户确认时的交互入口
/// CLI 层实现 ^ 提示符交互，非交互环境返回 Deny；GUI 层实现浏览器异步确认（TASK053 F2）
/// </summary>
public interface IPermissionConfirmationHandler {
    /// <summary>
    /// 请求用户确认工具执行权限 — 异步形态，适配 GUI 浏览器回帖 / TUI 按键 / CLI 提示符
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="confirmationPrompt">确认提示文本</param>
    /// <param name="cancellationToken">取消令牌 — 取消视为拒绝（fail-closed）</param>
    /// <returns>用户确认结果（动作 + 可选反馈说明）</returns>
    Task<PermissionConfirmResult> ConfirmAsync(string toolName, string confirmationPrompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// 权限确认结果 — 动作 + 用户补充说明（TASK069-P3，对齐基准 <c>PermissionPrompt</c> 的
/// <c>onSelect(value, feedback)</c>：选项可按 Tab 展开输入框填写说明）。
/// Feedback 会被 <see cref="PermissionConfirmAction.Deny"/> 拼进拒绝消息、被 Allow 追加到工具结果，
/// 让模型知道「用户不是简单拒绝，而是希望换个做法」。
/// </summary>
public sealed record PermissionConfirmResult {
    /// <summary>用户选择的确认动作。</summary>
    public required PermissionConfirmAction Action { get; init; }

    /// <summary>用户补充说明（空白归一为 null — 基准 trim 后为空即视为未填写）。</summary>
    public string? Feedback { get; init; }

    /// <summary>构造确认结果 — 反馈按 trim 归一（空白 → null）。</summary>
    /// <param name="action">确认动作。</param>
    /// <param name="feedback">用户补充说明（可空）。</param>
    public static PermissionConfirmResult Of(PermissionConfirmAction action, string? feedback = null) =>
        new() { Action = action, Feedback = string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim() };
}

/// <summary>
/// 权限确认动作
/// </summary>
public enum PermissionConfirmAction {
    /// <summary>拒绝执行</summary>
    [EnumValue("deny")]
    Deny,
    /// <summary>本次允许执行</summary>
    [EnumValue("allow")]
    Allow,
    /// <summary>始终允许（加入临时批准列表）</summary>
    [EnumValue("always_allow")]
    AlwaysAllow
}