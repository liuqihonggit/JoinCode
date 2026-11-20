namespace NativeTui.Tests.Repl;

/// <summary>
/// S8 权限键位扩展测试 — PermissionCoordinator 键位表新增面（对齐基准 defaultBindings.ts:131-148
/// Confirmation 上下文）：CycleFocus（tab 遍历）/ ToggleSelection（space 切换选中）/
/// ToggleExplanation（ctrl+e 解释展开）/ RequestModeCycle（shift+tab 权限模式循环请求事件）。
/// Begin 重置语义 + 既有 TCS 桥/焦点行为零破坏。
/// </summary>
public sealed class PermissionCoordinatorKeysTests
{
    /// <summary>构造挂起请求的 coordinator。</summary>
    private static PermissionCoordinator BeginCoordinator()
    {
        var coordinator = new PermissionCoordinator();
        coordinator.Begin(new PermissionRequest("Bash", "run tests"));
        return coordinator;
    }

    /// <summary>CycleFocus — tab 遍历选项（焦点 +1 环绕，与 MoveFocus(1) 等价）。</summary>
    [Fact]
    public void CycleFocus_AdvancesAndWraps()
    {
        var coordinator = BeginCoordinator();

        coordinator.CycleFocus();
        Assert.Equal(1, coordinator.FocusIndex);
        coordinator.CycleFocus();
        Assert.Equal(2, coordinator.FocusIndex);
        coordinator.CycleFocus();
        Assert.Equal(0, coordinator.FocusIndex);
    }

    /// <summary>ToggleSelection — 选中态翻转。</summary>
    [Fact]
    public void ToggleSelection_TogglesState()
    {
        var coordinator = BeginCoordinator();

        Assert.False(coordinator.SelectionToggled);
        coordinator.ToggleSelection();
        Assert.True(coordinator.SelectionToggled);
        coordinator.ToggleSelection();
        Assert.False(coordinator.SelectionToggled);
    }

    /// <summary>ToggleExplanation — 解释展开态翻转。</summary>
    [Fact]
    public void ToggleExplanation_TogglesState()
    {
        var coordinator = BeginCoordinator();

        Assert.False(coordinator.ExplanationExpanded);
        coordinator.ToggleExplanation();
        Assert.True(coordinator.ExplanationExpanded);
        coordinator.ToggleExplanation();
        Assert.False(coordinator.ExplanationExpanded);
    }

    /// <summary>Begin 重置 — 选中/解释态随新请求归位（对齐焦点重置语义）。</summary>
    [Fact]
    public void Begin_ResetsSelectionAndExplanation()
    {
        var coordinator = BeginCoordinator();
        coordinator.ToggleSelection();
        coordinator.ToggleExplanation();

        coordinator.Begin(new PermissionRequest("Edit", "write a.cs"));

        Assert.False(coordinator.SelectionToggled);
        Assert.False(coordinator.ExplanationExpanded);
        Assert.Equal(0, coordinator.FocusIndex);
    }

    /// <summary>RequestModeCycle — 事件触发（接线方订阅转发引擎回执通道）。</summary>
    [Fact]
    public void RequestModeCycle_RaisesEvent()
    {
        var coordinator = BeginCoordinator();
        var raised = 0;
        coordinator.ModeCycleRequested += () => raised++;

        coordinator.RequestModeCycle();
        coordinator.RequestModeCycle();

        Assert.Equal(2, raised);
    }

    /// <summary>RequestModeCycle — 无订阅安全（接线前 no-op 不抛）。</summary>
    [Fact]
    public void RequestModeCycle_WithoutSubscribers_SafeNoOp()
    {
        var coordinator = BeginCoordinator();

        coordinator.RequestModeCycle();

        Assert.True(coordinator.IsPending);
    }

    /// <summary>键位状态不影响既有 TCS 桥语义 — Wait/Complete 照常闭环。</summary>
    [Fact]
    public async Task NewKeyState_DoesNotAffectTcsBridge()
    {
        var coordinator = BeginCoordinator();
        coordinator.ToggleSelection();
        coordinator.ToggleExplanation();
        var task = coordinator.Wait();

        coordinator.Complete(PermissionChoice.Allow);

        Assert.Equal(PermissionChoice.Allow, await task);
        Assert.False(coordinator.IsPending);
    }

    // ---- TASK069-P3 拒绝反馈输入 ----

    /// <summary>Tab 在带输入框的档位切换输入态（对齐基准 CustomSelect onInputModeToggle）。</summary>
    [Fact]
    public void OnTab_TogglesFeedbackInput_OnFeedbackOption()
    {
        var coordinator = BeginCoordinator();

        coordinator.OnTab();
        Assert.True(coordinator.FeedbackInputActive);
        Assert.Equal(0, coordinator.FocusIndex);

        coordinator.OnTab();
        Assert.False(coordinator.FeedbackInputActive);
    }

    /// <summary>don't-ask-again 档没有输入框 — Tab 退回遍历语义（基准该档无 feedbackConfig）。</summary>
    [Fact]
    public void OnTab_FallsBackToCycleFocus_OnAlwaysAllow()
    {
        var coordinator = BeginCoordinator();
        coordinator.MoveFocus(1);
        Assert.Equal((int)PermissionChoice.AlwaysAllow, coordinator.FocusIndex);

        coordinator.OnTab();

        Assert.Equal((int)PermissionChoice.Deny, coordinator.FocusIndex);
        Assert.False(coordinator.FeedbackInputActive);
    }

    /// <summary>非输入态的字符不进反馈（避免普通快捷键污染用户说明）。</summary>
    [Fact]
    public void AppendFeedback_IgnoredWhenInactive()
    {
        var coordinator = BeginCoordinator();

        coordinator.AppendFeedback("x");

        Assert.Equal(string.Empty, coordinator.Feedback);
    }

    /// <summary>输入态追加 + 退格；空串退格安全。</summary>
    [Fact]
    public void AppendAndTrimFeedback()
    {
        var coordinator = BeginCoordinator();
        coordinator.OnTab();

        coordinator.AppendFeedback("改");
        coordinator.AppendFeedback("成备份");

        Assert.Equal("改成备份", coordinator.Feedback);

        coordinator.TrimFeedbackTail();
        Assert.Equal("改成备", coordinator.Feedback);

        coordinator.TrimFeedbackTail();
        coordinator.TrimFeedbackTail();
        coordinator.TrimFeedbackTail();
        coordinator.TrimFeedbackTail();

        Assert.Equal(string.Empty, coordinator.Feedback);
    }

    /// <summary>焦点移开收起输入态，已填文本保留（回来可继续编辑）。</summary>
    [Fact]
    public void MoveFocus_CollapsesInputButKeepsText()
    {
        var coordinator = BeginCoordinator();
        coordinator.OnTab();
        coordinator.AppendFeedback("保留");

        coordinator.MoveFocus(1);

        Assert.False(coordinator.FeedbackInputActive);
        Assert.Equal("保留", coordinator.Feedback);
    }

    /// <summary>占位文案按档位区分（对齐基准 accept / reject 两套 placeholder）。</summary>
    [Fact]
    public void FeedbackPlaceholder_DiffersPerChoice()
    {
        var coordinator = BeginCoordinator();

        var acceptHint = coordinator.FeedbackPlaceholder;
        coordinator.MoveFocus(2);
        var rejectHint = coordinator.FeedbackPlaceholder;

        Assert.Equal("告诉 Claude 接下来做什么", acceptHint);
        Assert.Equal("告诉 Claude 应该怎么做", rejectHint);
    }

    /// <summary>Begin 重置反馈（新请求清空上一条说明与输入态）。</summary>
    [Fact]
    public void Begin_ResetsFeedback()
    {
        var coordinator = BeginCoordinator();
        coordinator.OnTab();
        coordinator.AppendFeedback("上一次的说明");

        coordinator.Begin(new PermissionRequest("Edit", "write a.cs"));

        Assert.Equal(string.Empty, coordinator.Feedback);
        Assert.False(coordinator.FeedbackInputActive);
    }

    /// <summary>Complete 后反馈仍可读（bridge 在 Wait 返回后取走 — 不能被清空）。</summary>
    [Fact]
    public async Task Complete_KeepsFeedbackForBridge()
    {
        var coordinator = BeginCoordinator();
        coordinator.MoveFocus(2);
        coordinator.OnTab();
        coordinator.AppendFeedback("用 ls 代替");
        var task = coordinator.Wait();

        coordinator.Complete(PermissionChoice.Deny);

        Assert.Equal(PermissionChoice.Deny, await task);
        Assert.Equal("用 ls 代替", coordinator.Feedback);
    }
}
