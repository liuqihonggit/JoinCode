using JoinCode.Abstractions.Security.Permission;

namespace Host.Tests.Tui;

/// <summary>
/// 权限三档决策测试 — TUI 与 GUI/CLI 的权限语义对齐。
/// 回归背景（T3）：TUI 只有 允许(5分钟)/拒绝 两档，缺"始终允许"(24小时会话级)；
/// 且权限重发无上限，反复触发确认会无限弹窗循环（GUI 有 MaxPermissionRetries=3 保护）。
/// 枚举用 Abstractions 层 <see cref="PermissionConfirmAction"/> — TUI 不引用 GUI 专属类型。
/// </summary>
public class PermissionDecisionMappingTests
{
    [Fact]
    public void Allow_OneTime_MapsToFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), TuiModeRunner.GetApprovalDuration(PermissionConfirmAction.Allow));
    }

    [Fact]
    public void AlwaysAllow_MapsToTwentyFourHours_SessionLevel()
    {
        // 对齐 JccChatSession.AlwaysAllowDuration — 会话级长期批准
        Assert.Equal(TimeSpan.FromHours(24), TuiModeRunner.GetApprovalDuration(PermissionConfirmAction.AlwaysAllow));
    }

    [Fact]
    public void Deny_MapsToZero_NeverApproved()
    {
        Assert.Equal(TimeSpan.Zero, TuiModeRunner.GetApprovalDuration(PermissionConfirmAction.Deny));
    }

    [Fact]
    public void MaxPermissionRetries_AlignsWithGui()
    {
        Assert.Equal(3, TuiModeRunner.MaxPermissionRetries);
    }
}
