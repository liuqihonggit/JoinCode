namespace NativeTui.Tests.Repl;

/// <summary>
/// 权限模式循环测试 — 对齐基准 getNextPermissionMode.ts（Shift+Tab carousel）：
/// 非 ant 完整链 default → acceptEdits → plan → bypassPermissions → auto → default，
/// bypass/auto 按可用性开关短路（不可用即跳过），未知模式回 default。
/// </summary>
public sealed class PermissionModeCyclerTests
{
    [Theory]
    [InlineData(PermissionMode.Default, PermissionMode.AcceptEdits)]
    [InlineData(PermissionMode.AcceptEdits, PermissionMode.Plan)]
    [InlineData(PermissionMode.DontAsk, PermissionMode.Default)]
    [InlineData(PermissionMode.Auto, PermissionMode.Default)]
    public void Next_FollowsBaselineCarousel(PermissionMode current, PermissionMode expected)
    {
        Assert.Equal(expected, PermissionModeCycler.Next(current, bypassAvailable: true, autoAvailable: true));
    }

    [Fact]
    public void Plan_WithAllModesAvailable_CyclesToBypassPermissions()
    {
        Assert.Equal(PermissionMode.BypassPermissions, PermissionModeCycler.Next(PermissionMode.Plan, bypassAvailable: true, autoAvailable: true));
    }

    [Fact]
    public void Plan_WithoutBypass_CyclesToAuto()
    {
        Assert.Equal(PermissionMode.Auto, PermissionModeCycler.Next(PermissionMode.Plan, bypassAvailable: false, autoAvailable: true));
    }

    [Fact]
    public void Plan_WithoutBypassOrAuto_FallsBackToDefault()
    {
        Assert.Equal(PermissionMode.Default, PermissionModeCycler.Next(PermissionMode.Plan, bypassAvailable: false, autoAvailable: false));
    }

    [Fact]
    public void BypassPermissions_WithAuto_CyclesToAuto()
    {
        Assert.Equal(PermissionMode.Auto, PermissionModeCycler.Next(PermissionMode.BypassPermissions, bypassAvailable: true, autoAvailable: true));
    }

    [Fact]
    public void BypassPermissions_WithoutAuto_FallsBackToDefault()
    {
        Assert.Equal(PermissionMode.Default, PermissionModeCycler.Next(PermissionMode.BypassPermissions, bypassAvailable: true, autoAvailable: false));
    }
}
