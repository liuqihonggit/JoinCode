namespace NativeTui.Core.Repl;

/// <summary>
/// 需求澄清模式装饰器 — /?? 与 /ask 拦截为 TUI 原生澄清（TASK078）：
/// CLI 版 AskClarifyCommand 是直写终端的交互循环（TerminalHelper.ReadLine 阻塞等输入，
/// TUI 会话内不可见且卡死），本装饰器把 /?? 参数组合澄清系统提示词后以**正常回合**提交 —
/// 多轮澄清 = 会话连续对话（用户在输入框应答，AI 的澄清问题作为普通 assistant 消息上屏，
/// 需求明确后 AI 输出【需求已明确】标记总结需求清单）。
/// 提示词由宿主注入（composition AskClarifyPrompts — 单一数据源，本层不依赖 composition）。
/// 其余输入原样透传；非拦截成员全部委托内层。
/// </summary>
public sealed class ClarifyModeEngine(
    IReplEngine inner,
    string systemPrompt) : IReplEngine
{
    /// <summary>拦截的命令名（/?? 主名 + /ask 别名 — 对齐 composition ChatCommandName AskClarify）。</summary>
    private static readonly string[] ClarifyCommands = ["/??", "/ask"];

    /// <summary>空参数首轮指令 — 用户尚未描述需求，引导 AI 主动发问。</summary>
    private const string EmptyArgsInstruction = "（用户尚未描述需求 — 请先用 AskUserQuestion 工具了解用户想构建什么）";

    /// <summary>外壳绑定透传 — 叠层装饰时传播给内层 CommandPickerEngine（对齐 CommandPickerEngine.Attach 先例）。</summary>
    public void Attach(ReplShell shell) {
        if (inner is CommandPickerEngine nested) {
            nested.Attach(shell);
        }
    }

    /// <summary>提交回合 — /?? //ask 开头时组合澄清回合，其余原样透传。</summary>
    public void Submit(string text) {
        var composed = TryComposeClarifyTurn(text);
        if (composed is not null) {
            inner.Submit(composed);
            return;
        }

        inner.Submit(text);
    }

    /// <summary>提交回合（带附件）— 澄清回合携带图片附件透传。</summary>
    public void Submit(string text, IReadOnlyList<BridgeImageAttachment>? images) {
        var composed = TryComposeClarifyTurn(text);
        if (composed is not null) {
            inner.Submit(composed, images);
            return;
        }

        inner.Submit(text, images);
    }

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

    /// <summary>澄清回合组合 — 输入以 /?? 或 /ask 开头时剥离命令名，组合系统提示词 + 需求；
    /// 其余输入返回 null 走原样提交。</summary>
    private string? TryComposeClarifyTurn(string text) {
        var trimmed = text.TrimStart();
        foreach (var command in ClarifyCommands) {
            if (!trimmed.StartsWith(command, StringComparison.Ordinal)) {
                continue;
            }

            var rest = trimmed[command.Length..].Trim();
            var need = rest.Length > 0 ? rest : EmptyArgsInstruction;
            return $"{systemPrompt}\n\n---\n\n用户需求: {need}";
        }

        return null;
    }
}
