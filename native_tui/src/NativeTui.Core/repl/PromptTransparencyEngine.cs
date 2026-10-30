namespace NativeTui.Core.Repl;

/// <summary>
/// 提示词透明化装饰器 — /prompt 拦截（TASK079 用户指令：用户应能清晰知道注入了什么）：
/// 把首次注入的系统提示词全文 + 工具清单 dump 进 transcript（AssistantMessageItem，
/// ctrl+o transcript 模态可回看）— 数据由宿主注入（引擎会话服务：ISystemPromptProvider
/// 真实 sections + IMcpToolRegistry 真实工具清单，单一数据源不硬编码）。
/// 其余输入原样透传；非拦截成员全部委托内层。
/// </summary>
public sealed class PromptTransparencyEngine(
    IReplEngine inner,
    Func<string> promptSnapshotProvider) : IReplEngine
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

    /// <summary>提交回合 — /prompt 时把提示词快照 dump 进 transcript，其余原样透传。</summary>
    public void Submit(string text) {
        if (text.Trim().Equals("/prompt", StringComparison.OrdinalIgnoreCase)) {
            _shell?.AddTranscriptNote(promptSnapshotProvider());
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
