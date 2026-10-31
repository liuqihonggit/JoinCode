namespace NativeTui.Core.Repl;

/// <summary>
/// 提示词透明化装饰器 — /prompt 拦截（TASK079 用户指令：用户应能清晰知道注入了什么）：
/// /prompt 切换注入上下文折叠项（PromptContextItem — 首帧插入，快照由宿主注入）展开/收起，
/// 用户经终端滚动回看展开内容（主屏不启用鼠标上报 — 与原生选择复制互斥）。
/// 其余输入原样透传；非拦截成员全部委托内层。
/// </summary>
public sealed class PromptTransparencyEngine(IReplEngine inner) : IReplEngine
{
    /// <summary>外壳（延迟绑定 — Attach 注入；dump 落版经外壳 AddMessage 主线程通道）。</summary>
    private ReplShell? _shell;

    /// <summary>绑定外壳（new ReplShell(装饰器) 之后调用一次）— 叠层装饰时递归传播内层同族。</summary>
    public void Attach(ReplShell shell) {
        _shell = shell;
        if (inner is CommandPickerEngine nested) {
            nested.Attach(shell);
        }
    }

    /// <summary>提交回合 — /prompt 另开一屏（transcript 备用屏）查看首次注入内容，其余原样透传。</summary>
    public void Submit(string text) {
        if (text.Trim().Equals("/prompt", StringComparison.OrdinalIgnoreCase)) {
            _shell?.TogglePromptContext();
            return;
        }

        inner.Submit(text);
    }

    /// <summary>提交回合（带附件）— 原样透传。</summary>
    public void Submit(string text, IReadOnlyList<BridgeImageAttachment>? images) => inner.Submit(text, images);

    /// <summary>打断当前回合。</summary>
    public void Interrupt() => inner.Interrupt();

    /// <summary>请求权限模式切换。</summary>
    public void SetPermissionMode(string mode) => inner.SetPermissionMode(mode);

    /// <summary>排空待应用事件。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents() => inner.DrainEvents();

    /// <summary>排空诊断旁路。</summary>
    public IReadOnlyList<string> DrainDiagnostics() => inner.DrainDiagnostics();

    /// <summary>停止后台任务。</summary>
    public void StopBackgroundTask(string kind, string taskId) => inner.StopBackgroundTask(kind, taskId);

    /// <summary>开始新会话。</summary>
    public void StartNewConversation() => inner.StartNewConversation();

    /// <summary>引擎存活。</summary>
    public bool IsAlive => inner.IsAlive;
}
