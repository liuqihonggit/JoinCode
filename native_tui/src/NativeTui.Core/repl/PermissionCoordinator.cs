namespace NativeTui.Core.Repl;

/// <summary>
/// 权限确认选择（三档）— 对齐基准 FallbackPermissionRequest 选项值：
/// yes → Allow / yes-dont-ask-again → AlwaysAllow / no → Deny（Esc 取消同 Deny）。
/// </summary>
public enum PermissionChoice
{
    /// <summary>允许（本次执行）。</summary>
    Allow,

    /// <summary>始终允许（会话级规则 — Host 映射引擎 AlwaysAllow）。</summary>
    AlwaysAllow,

    /// <summary>拒绝（含 Esc 取消）。</summary>
    Deny,
}

/// <summary>挂起中的权限请求（工具名 + 描述参数 + 触发原因规则内容）。</summary>
/// <param name="ToolName">待确认工具名。</param>
/// <param name="Description">确认提示文本。</param>
/// <param name="RuleContent">触发本次确认的规则内容（TASK069-P5 — 如 WebFetch 的
/// <c>domain:example.com</c>；空串 = 无结构化来源，对话框不渲染原因区）。</param>
public sealed record PermissionRequest(string ToolName, string Description, string RuleContent = "");

/// <summary>
/// 权限对话框协调器（M7-3）— TCS 桥 + 对话框状态：
/// Begin 建立挂起请求（旧等待者取消，对齐青蛙TUI DialogRequest 取代语义），
/// Complete 解除等待并清空（幂等）；焦点三档环绕（Select 焦点等价物，Begin 重置 0）。
/// 消费方：ReplShell 模态改道（数字/Esc/上下/Enter 路由对话框）+ Host 层 TuiPermissionConfirmationHandler
///（引擎 Confirm 回调 → Begin → Wait 同步阻塞 → 用户按键 Complete）。
/// S8 键位表扩展（对齐基准 defaultBindings.ts:131-148 Confirmation 上下文）：
/// CycleFocus（tab 遍历）/ ToggleSelection（space 切换选中）/ ToggleExplanation（ctrl+e 解释展开）/
/// RequestModeCycle（shift+tab 权限模式循环请求事件 — ReplShell 接线转发引擎回执通道）。
/// </summary>
public sealed class PermissionCoordinator
{
    /// <summary>选项档数（Allow/AlwaysAllow/Deny）。</summary>
    public const int OptionCount = 3;

    private TaskCompletionSource<PermissionChoice>? _pending;

    /// <summary>当前挂起请求（无挂起为 null）。</summary>
    public PermissionRequest? Request { get; private set; }

    /// <summary>焦点选项索引（0-2，环绕移动）。</summary>
    public int FocusIndex { get; private set; }

    /// <summary>Space 切换选中态（S8 — 基准 space: confirm:toggle；Begin 重置 false）。</summary>
    public bool SelectionToggled { get; private set; }

    /// <summary>Ctrl+E 解释展开态（S8 — 基准 ctrl+e: confirm:toggleExplanation，命令详情展开；Begin 重置 false）。</summary>
    public bool ExplanationExpanded { get; private set; }

    /// <summary>用户补充说明（P3 — 选项按 Tab 展开输入框填写；Begin 重置，Complete 后由 TuiPermissionBridge 读取）。</summary>
    public string Feedback { get; private set; } = string.Empty;

    /// <summary>反馈输入态（P3 — Tab 在有反馈的选项上切换；焦点移开即收起）。</summary>
    public bool FeedbackInputActive { get; private set; }

    /// <summary>当前焦点档是否支持反馈输入（P3 — 基准 FallbackPermissionRequest 只给 Yes/No 配
    /// <c>feedbackConfig</c>，don't-ask-again 档没有 → Tab 在该档退回遍历语义）。</summary>
    public bool FocusSupportsFeedback => FocusIndex != (int)PermissionChoice.AlwaysAllow;

    /// <summary>反馈输入占位文案（按焦点档 — 对齐基准 accept/reject 两套 placeholder）。</summary>
    public string FeedbackPlaceholder => FocusIndex == (int)PermissionChoice.Deny
        ? "告诉 Claude 应该怎么做"
        : "告诉 Claude 接下来做什么";

    /// <summary>Shift+Tab 权限模式循环请求（S8 — 基准 shift+tab: confirm:cycleMode；
    /// ReplShell 接线订阅转发 CyclePermissionMode 引擎回执通道 — 接线未决问题记录）。</summary>
    public event Action? ModeCycleRequested;

    /// <summary>是否挂起中（对话框模态路由依据）。</summary>
    public bool IsPending => _pending is not null;

    /// <summary>等待用户选择（Host handler 同步阻塞 GetAwaiter().GetResult()；无挂起时为已完成 Deny 的占位任务）。</summary>
    public Task<PermissionChoice> Wait() =>
        (_pending ?? CompletedDeny).Task;

    /// <summary>已完成 Deny 的占位任务（无挂起 Wait 的安全值）。</summary>
    private static readonly TaskCompletionSource<PermissionChoice> CompletedDeny = CreateCompleted(PermissionChoice.Deny);

    /// <summary>Begin 挂起请求 — 取消旧等待者 + 焦点/选中/解释态重置。</summary>
    public void Begin(PermissionRequest request)
    {
        _pending?.TrySetCanceled();
        _pending = new TaskCompletionSource<PermissionChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        Request = request;
        FocusIndex = 0;
        SelectionToggled = false;
        ExplanationExpanded = false;
        Feedback = string.Empty;
        FeedbackInputActive = false;
    }

    /// <summary>Complete 解除等待 + 清空状态（幂等 — 二次调用 no-op）。</summary>
    public void Complete(PermissionChoice choice)
    {
        if (_pending is null)
        {
            return;
        }

        _pending.TrySetResult(choice);
        _pending = null;
        Request = null;
    }

    /// <summary>焦点环绕移动（上 = -1 / 下 = +1）— 焦点离开即收起反馈输入态（已填文本保留）。</summary>
    public void MoveFocus(int delta)
    {
        FocusIndex = ((FocusIndex + delta) % OptionCount + OptionCount) % OptionCount;
        FeedbackInputActive = false;
    }

    /// <summary>Tab 遍历选项（S8 — 基准 tab: confirm:nextField；焦点 +1 环绕，键位表独立入口）。</summary>
    public void CycleFocus() => MoveFocus(1);

    /// <summary>
    /// Tab 双重语义（P3 — 对齐基准 CustomSelect <c>onInputModeToggle</c>：Tab 在带输入框的选项上
    /// 进/出输入态）：焦点档支持反馈 → 切换输入态；否则退回 S8 的 <see cref="CycleFocus"/> 遍历语义。
    /// </summary>
    public void OnTab()
    {
        if (FocusSupportsFeedback)
        {
            FeedbackInputActive = !FeedbackInputActive;
            return;
        }

        CycleFocus();
    }

    /// <summary>追加反馈文本（仅在输入态生效 — 非输入态的字符不污染反馈，仍由调用方吞掉）。</summary>
    /// <param name="text">本次输入文本（字符或粘贴块）。</param>
    public void AppendFeedback(string text)
    {
        if (!FeedbackInputActive || string.IsNullOrEmpty(text))
        {
            return;
        }

        Feedback += text;
    }

    /// <summary>反馈退格（空串静默 — 输入态下不关闭对话框）。</summary>
    public void TrimFeedbackTail()
    {
        if (!FeedbackInputActive || Feedback.Length == 0)
        {
            return;
        }

        Feedback = Feedback[..^1];
    }

    /// <summary>Space 切换选中（S8 — 基准 space: confirm:toggle；选中态翻转，Begin 重置）。</summary>
    public void ToggleSelection() => SelectionToggled = !SelectionToggled;

    /// <summary>Ctrl+E 解释展开切换（S8 — 基准 ctrl+e: confirm:toggleExplanation；命令详情展开态翻转，Begin 重置）。</summary>
    public void ToggleExplanation() => ExplanationExpanded = !ExplanationExpanded;

    /// <summary>Shift+Tab 权限模式循环请求（S8 — 基准 shift+tab: confirm:cycleMode；触发事件供接线方转发引擎回执通道）。</summary>
    public void RequestModeCycle() => ModeCycleRequested?.Invoke();

    /// <summary>构造已完成结果的占位 TCS。</summary>
    private static TaskCompletionSource<PermissionChoice> CreateCompleted(PermissionChoice choice)
    {
        var source = new TaskCompletionSource<PermissionChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.TrySetResult(choice);
        return source;
    }
}
