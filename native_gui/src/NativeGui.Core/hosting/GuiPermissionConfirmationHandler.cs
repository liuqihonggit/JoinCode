namespace JoinCode.Gui.Hosting;

/// <summary>
/// GUI 权限确认处理器 — 把引擎的同步确认点（<see cref="IPermissionConfirmationHandler"/>）
/// 转接为宿主注入的异步委托（WebUI 模式 = PermissionPromptBroker 浏览器回帖；Avalonia 模式 = PermissionDialog）。
/// 未注入委托 / 取消 / 非法决策一律拒绝（fail-closed）。
/// 批准时对齐 CLI：临时批准工具 + 会话级批准危险等级（同一 <see cref="IToolPermissionManager"/> 数据源）。
/// </summary>
[Register(typeof(IPermissionConfirmationHandler), JoinCode.Abstractions.Attributes.ServiceLifetime.Singleton)]
public sealed class GuiPermissionConfirmationHandler : IPermissionConfirmationHandler {
    private static readonly TimeSpan AllowDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan AlwaysAllowDuration = TimeSpan.FromMinutes(30);

    private readonly IToolPermissionManager? _permissionManager;

    /// <summary>宿主注入的确认委托 — 由 <see cref="IJccChatSession.PermissionConfirmationHandler"/> 转发赋值</summary>
    public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? Handler { get; set; }

    /// <summary>初始化 <see cref="GuiPermissionConfirmationHandler"/> 实例</summary>
    /// <param name="permissionManager">可选的工具权限管理器，用于临时批准工具或危险等级</param>
    public GuiPermissionConfirmationHandler(IToolPermissionManager? permissionManager = null) {
        _permissionManager = permissionManager;
    }

    /// <inheritdoc/>
    public async Task<PermissionConfirmAction> ConfirmAsync(string toolName, string confirmationPrompt, CancellationToken cancellationToken = default) {
        var handler = Handler;
        if (handler is null || cancellationToken.IsCancellationRequested)
            return PermissionConfirmAction.Deny;

        PermissionConfirmationDecision decision;
        try {
            decision = await handler(
                new PermissionConfirmationRequest(toolName, confirmationPrompt, RequestId: null, RuleContent: null) {
                    DangerLevel = DangerLevelPromptParser.ParseLevelFromPrompt(confirmationPrompt)
                }).WaitAsync(cancellationToken);
        } catch (OperationCanceledException) {
            return PermissionConfirmAction.Deny;
        }

        if (decision == PermissionConfirmationDecision.Deny)
            return PermissionConfirmAction.Deny;

        ApproveDecision(toolName, confirmationPrompt, decision == PermissionConfirmationDecision.AlwaysAllow);
        return decision == PermissionConfirmationDecision.AlwaysAllow
            ? PermissionConfirmAction.AlwaysAllow
            : PermissionConfirmAction.Allow;
    }

    /// <summary>批准副作用 — 临时批准工具（时长按决策）+ 会话级批准危险等级，与 CLI 同源</summary>
    private void ApproveDecision(string toolName, string confirmationPrompt, bool always) {
        if (_permissionManager is null)
            return;

        _permissionManager.ApproveToolTemporarily(toolName, always ? AlwaysAllowDuration : AllowDuration);

        var level = DangerLevelPromptParser.ParseLevelFromPrompt(confirmationPrompt);
        if (level is not null)
            _permissionManager.ApproveLevelTemporarily(level.Value);
    }
}
