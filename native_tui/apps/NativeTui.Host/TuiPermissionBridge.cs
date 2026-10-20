namespace NativeTui.Host;

/// <summary>
/// TUI 权限桥（M7-3）— 引擎线程与 TUI 主线程的交互桥：
/// 引擎 Confirm 回调（回合执行线程）→ shell.BeginPermissionRequest 挂起对话框 →
/// 同步阻塞等用户按键（TCS 异步续延，主循环线程解除）→ 返回引擎 PermissionConfirmAction。
/// shell 未绑定（极早期请求/退出中）直接 Deny 降级 — 防引擎线程死等。
/// 与 CLI CliPermissionConfirmationHandler 的 IConfirmationGate 单通道路由同构（ADR 0143 进程内闭环）。
/// </summary>
public class TuiPermissionBridge {
    /// <summary>绑定的 REPL 外壳（volatile — Program 在 shell 构造后绑定，引擎线程并发读）。</summary>
    private volatile ReplShell? _shell;

    /// <summary>绑定外壳（Program 交互模式组装点 — shell 构造后、Run 前调用）。</summary>
    public void Bind(ReplShell shell) => _shell = shell;

    /// <summary>
    /// 挂起权限对话框并等待用户选择 — 工具名/描述透传对话框，按键解除后映射引擎动作；
    /// 未绑定 shell 时 Deny（降级不阻塞）。virtual 供测试替身（FakeBridge 先例）。
    /// </summary>
    public virtual PermissionConfirmAction Confirm(string toolName, string confirmationPrompt) {
        var shell = _shell;
        if (shell is null)
            return PermissionConfirmAction.Deny;

        shell.BeginPermissionRequest(toolName, confirmationPrompt);
        var choice = shell.Permission.Wait().GetAwaiter().GetResult();
        return choice switch {
            PermissionChoice.Allow => PermissionConfirmAction.Allow,
            PermissionChoice.AlwaysAllow => PermissionConfirmAction.AlwaysAllow,
            _ => PermissionConfirmAction.Deny,
        };
    }
}
