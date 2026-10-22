namespace NativeTui.Core.Repl;

/// <summary>
/// 命令交互对话框协调器（TASK065 F2）— 斜杠命令 confirm/prompt 回调的 TCS 桥 + 对话框状态：
/// 引擎回合线程（SlashCommandRunner 命令侧同步回调）→ ReplShell.BeginCommandConfirm/BeginCommandPrompt
/// 挂起对话框 → 主循环模态按键路由 → Complete 解除等待（对齐 PermissionCoordinator 形态：
/// Begin 取代旧等待者 / Complete 幂等 / 无挂起 Wait 返回已完成降级占位防调用线程死等）。
/// confirm 两档焦点（Yes/No）环绕；prompt 输入缓冲尾追加/尾退格（光标移动属对话框增强遗留项）。
/// </summary>
public sealed class CommandDialogCoordinator
{
    /// <summary>confirm 选项档数（Yes/No）。</summary>
    public const int ConfirmOptionCount = 2;

    private TaskCompletionSource<bool>? _confirmPending;
    private TaskCompletionSource<string?>? _promptPending;

    /// <summary>已完成 false 的占位任务（无挂起 WaitConfirm 的安全值）。</summary>
    private static readonly TaskCompletionSource<bool> CompletedFalse = CreateCompleted(false);

    /// <summary>已完成 null 的占位任务（无挂起 WaitPrompt 的安全值）。</summary>
    private static readonly TaskCompletionSource<string?> CompletedNull = CreateCompleted<string?>(null);

    /// <summary>当前对话框消息（确认问题/输入提示；无挂起为 null）。</summary>
    public string? Message { get; private set; }

    /// <summary>prompt 输入缓冲（挂起期间的编辑文本，Enter 提交）。</summary>
    public string PromptInput { get; private set; } = string.Empty;

    /// <summary>confirm 焦点选项索引（0=Yes / 1=No，环绕移动，Begin 重置 0）。</summary>
    public int ConfirmFocusIndex { get; private set; }

    /// <summary>是否挂起中（对话框模态路由依据）。</summary>
    public bool IsPending => _confirmPending is not null || _promptPending is not null;

    /// <summary>挂起的是输入对话框（false = 确认对话框）。</summary>
    public bool IsPrompt => _promptPending is not null;

    /// <summary>等待确认结果（命令回调线程同步阻塞 GetAwaiter().GetResult()；无挂起时为已完成 false 占位任务）。</summary>
    public Task<bool> WaitConfirm() =>
        (_confirmPending ?? CompletedFalse).Task;

    /// <summary>等待输入结果（无挂起时为已完成 null 占位任务；null = Esc 取消）。</summary>
    public Task<string?> WaitPrompt() =>
        (_promptPending ?? CompletedNull).Task;

    /// <summary>Begin 确认请求 — 取消旧等待者（两种挂起态互斥同取缔）+ 焦点重置。</summary>
    public void BeginConfirm(string message)
    {
        CancelPending();
        _confirmPending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Message = message;
        ConfirmFocusIndex = 0;
    }

    /// <summary>Begin 输入请求 — 取消旧等待者 + 输入缓冲清空。</summary>
    public void BeginPrompt(string message)
    {
        CancelPending();
        _promptPending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Message = message;
        PromptInput = string.Empty;
    }

    /// <summary>Complete 确认 — 解除等待 + 清空状态（幂等 — 二次调用 no-op）。</summary>
    public void CompleteConfirm(bool accepted)
    {
        if (_confirmPending is null)
        {
            return;
        }

        _confirmPending.TrySetResult(accepted);
        _confirmPending = null;
        Message = null;
    }

    /// <summary>Complete 输入 — 解除等待 + 清空状态（幂等；input null = 取消语义）。</summary>
    public void CompletePrompt(string? input)
    {
        if (_promptPending is null)
        {
            return;
        }

        _promptPending.TrySetResult(input);
        _promptPending = null;
        Message = null;
        PromptInput = string.Empty;
    }

    /// <summary>confirm 焦点环绕移动（上 = -1 / 下 = +1，两档）。</summary>
    public void MoveConfirmFocus(int delta) =>
        ConfirmFocusIndex = ((ConfirmFocusIndex + delta) % ConfirmOptionCount + ConfirmOptionCount) % ConfirmOptionCount;

    /// <summary>prompt 输入缓冲尾追加一个字符。</summary>
    public void AppendPromptInput(char c) => PromptInput += c;

    /// <summary>prompt 输入缓冲尾退格（空缓冲 no-op）。</summary>
    public void BackspacePromptInput()
    {
        if (PromptInput.Length > 0)
        {
            PromptInput = PromptInput[..^1];
        }
    }

    /// <summary>取消任何挂起等待者（Begin 取代语义 + 回合结束自动收尾共用）。</summary>
    private void CancelPending()
    {
        _confirmPending?.TrySetCanceled();
        _promptPending?.TrySetCanceled();
        _confirmPending = null;
        _promptPending = null;
    }

    /// <summary>构造已完成结果的占位 TCS。</summary>
    private static TaskCompletionSource<T> CreateCompleted<T>(T result)
    {
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.TrySetResult(result);
        return source;
    }
}
