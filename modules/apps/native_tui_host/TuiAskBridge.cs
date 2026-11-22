namespace NativeTui.Host;

/// <summary>
/// TUI 问答桥 — 引擎问答线程与 TUI 主线程的交互桥（对齐 TuiPermissionBridge 形态）：
/// 引擎线程调用 Ask → shell.AskUserQuestions 挂起问答对话框并同步阻塞等用户按键
/// （主循环线程驱动按键/后台回调唤醒）→ 返回 TuiAskResult。
/// 同时持有后台通知队列探测面：问答挂起期间有新通知 → ReplShell 帧拍自动关闭对话框返回 Deferred，
/// 让 Agent 循环先消费回调再重新提问（不假定答案、不设超时）。
/// shell 未绑定（极早期/退出中）返回 Failure 诱导 — 防引擎线程死等。
/// </summary>
public class TuiAskBridge {
    /// <summary>绑定的 REPL 外壳（volatile — Program 在 shell 构造后绑定，引擎线程并发读）。</summary>
    private volatile ReplShell? _shell;

    /// <summary>后台通知队列（volatile — 引擎容器组装期附加，主循环帧拍并发读）。</summary>
    private volatile IAgentNotificationQueue? _notificationQueue;

    /// <summary>绑定外壳（Program 交互模式组装点 — shell 构造后、Run 前调用）。</summary>
    public void Bind(ReplShell shell) => _shell = shell;

    /// <summary>附加后台通知队列（EngineSession 组装期从引擎容器解析；null 表示无队列，探测恒 false）。</summary>
    public void AttachNotificationQueue(IAgentNotificationQueue? notificationQueue) => _notificationQueue = notificationQueue;

    /// <summary>是否有待处理后台通知（ReplShell 帧拍探测 — true 时提前关闭挂起问答）。</summary>
    public bool HasPendingNotification => _notificationQueue?.HasPendingNotifications == true;

    /// <summary>
    /// 挂起问答对话框并同步等待用户作答；未绑定 shell 或空问题集返回 Failure（含恢复诱导）。
    /// 取消令牌透传给 shell：只置取消请求，主循环帧拍关闭对话框，避免引擎线程跨线程改 UI。
    /// virtual 供测试替身（FakeBridge 先例）。
    /// </summary>
    /// <param name="questions">问题列表</param>
    /// <param name="cancellationToken">引擎回合取消令牌</param>
    public virtual TuiAskResult Ask(IReadOnlyList<TuiAskQuestion> questions, CancellationToken cancellationToken = default) {
        if (questions.Count == 0)
            return TuiAskResult.Failure("没有可提问的问题；请重新组织问题后再调用。");

        var shell = _shell;
        if (shell is null)
            return TuiAskResult.Failure("TUI 问答界面尚未绑定（引擎启动中或正在退出）；请回到对话继续，稍后重新提问。");

        return shell.AskUserQuestions(questions, cancellationToken);
    }
}
