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
}
