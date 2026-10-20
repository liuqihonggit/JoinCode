namespace NativeTui.Host;

/// <summary>
/// TUI 权限确认处理器（M7-3）— 引擎 PendingConfirmation 交互闭环的引擎侧实现：
/// 转发 TuiPermissionBridge（对话框交互）+ 按结果临时批准工具/等级
/// （Allow 1 分钟 / AlwaysAllow 30 分钟 + prompt 内 levelTag 同级批准 — 对齐 CLI CliPermissionConfirmationHandler 先例，
/// 关闭"允许后同回合重跑管道直接通过"闭环）。
/// </summary>
public sealed class TuiPermissionConfirmationHandler(TuiPermissionBridge bridge, IToolPermissionManager? permissionManager = null)
    : IPermissionConfirmationHandler {

    /// <summary>Allow 档临时批准时长（CLI 先例 1 分钟）。</summary>
    private static readonly TimeSpan AllowDuration = TimeSpan.FromMinutes(1);

    /// <summary>AlwaysAllow 档临时批准时长（CLI 先例 30 分钟）。</summary>
    private static readonly TimeSpan AlwaysAllowDuration = TimeSpan.FromMinutes(30);

    /// <summary>交互桥（shell 对话框）。</summary>
    public TuiPermissionBridge Bridge { get; } = bridge;

    /// <summary>工具权限管理器（可选 — 无权限系统时仅交互不批准）。</summary>
    public IToolPermissionManager? PermissionManager { get; } = permissionManager;

    /// <summary>
    /// 挂起对话框等待用户确认并按结果临时批准 — 用户选择经 bridge 返回，
    /// 临时批准使 PermissionAwareToolExecutor 允许后重跑权限管道对本工具直接 Granted。
    /// </summary>
    public PermissionConfirmAction Confirm(string toolName, string confirmationPrompt) {
        var action = Bridge.Confirm(toolName, confirmationPrompt);
        ApplyTemporaryApproval(toolName, confirmationPrompt, action);
        return action;
    }

    /// <summary>
    /// 异步确认入口（ADR 0142 接口形态）— 取消视为拒绝（fail-closed，对齐 CLI 先例）；
    /// 同步转发 bridge 挂起对话框（TCS 异步续延，引擎回合线程等按键解除）。
    /// </summary>
    public Task<PermissionConfirmAction> ConfirmAsync(string toolName, string confirmationPrompt, CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(PermissionConfirmAction.Deny);
        return Task.FromResult(Confirm(toolName, confirmationPrompt));
    }

    /// <summary>按确认结果应用临时批准（工具档位时长 + levelTag 同级批准 — Deny 不批准）。</summary>
    private void ApplyTemporaryApproval(string toolName, string confirmationPrompt, PermissionConfirmAction action) {
        if (PermissionManager is null)
            return;

        if (action == PermissionConfirmAction.Allow)
            PermissionManager.ApproveToolTemporarily(toolName, AllowDuration);
        else if (action == PermissionConfirmAction.AlwaysAllow)
            PermissionManager.ApproveToolTemporarily(toolName, AlwaysAllowDuration);

        var level = DangerLevelPromptParser.ParseLevelFromPrompt(confirmationPrompt);
        if (level is not null)
            PermissionManager.ApproveLevelTemporarily(level.Value);
    }
}
