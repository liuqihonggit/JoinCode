using JoinCode.Abstractions.Security.Permission;

namespace Host.Tests.Tui.Views;

/// <summary>
/// PermissionDialogView 单元测试 — 验证权限弹窗显示/隐藏/取消行为。
/// P0-2 权限闭环：ShowAsync 显示弹窗 → 用户决策 → Hide 隐藏。
/// </summary>
using System.Reflection;

public class PermissionDialogViewTests
{
    [Fact]
    public void Initial_Invisible()
    {
        var dialog = new PermissionDialogView();
        Assert.False(dialog.TerminalView.Visible);
    }

    [Fact]
    public async Task ShowAsync_MakesVisible()
    {
        var dialog = new PermissionDialogView();
        using var cts = new CancellationTokenSource();
        var task = dialog.ShowAsync("Read", "读取文件 test.txt", cts.Token);

        Assert.True(dialog.TerminalView.Visible);

        cts.Cancel();
        await task;
    }

    [Fact]
    public async Task ShowAsync_Cancelled_ReturnsFalse()
    {
        var dialog = new PermissionDialogView();
        using var cts = new CancellationTokenSource();
        var task = dialog.ShowAsync("Write", "写入文件", cts.Token);

        cts.Cancel();
        var result = await task;
        Assert.False(result);
    }

    [Fact]
    public void Hide_MakesInvisible()
    {
        var dialog = new PermissionDialogView();
        using var cts = new CancellationTokenSource();
        _ = dialog.ShowAsync("Read", "test", cts.Token);

        dialog.Hide();
        Assert.False(dialog.TerminalView.Visible);
        cts.Cancel();
    }

    [Fact]
    public void TerminalView_ContainsButtons()
    {
        var dialog = new PermissionDialogView();
        var snapshot = ViewTreeSerializer.Serialize(dialog.TerminalView);
        Assert.Contains("Button", snapshot);
        Assert.Contains("允许", snapshot);
        Assert.Contains("拒绝", snapshot);
    }

    [Fact]
    public void ShowWithAlways_RendersThreeTierChoices()
    {
        // T3 三档决策 — 始终允许(24h会话级)按钮必须存在
        var dialog = new PermissionDialogView();
        var snapshot = ViewTreeSerializer.Serialize(dialog.TerminalView);
        Assert.Contains("始终允许", snapshot);
    }

    [Fact]
    public async Task ShowAlwaysAsync_AlwaysButton_CompletesWithAlwaysAllow()
    {
        var dialog = new PermissionDialogView();
        using var cts = new CancellationTokenSource();
        var task = dialog.ShowWithDecisionAsync("Bash", "执行命令 npm test", cts.Token);
        Assert.True(dialog.TerminalView.Visible);

        InvokeDecision(dialog, "OnAlwaysAllow");
        var decision = await task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(PermissionConfirmAction.AlwaysAllow, decision);
        Assert.False(dialog.TerminalView.Visible);
    }

    [Fact]
    public async Task ShowAlwaysAsync_AllowButton_CompletesWithAllow()
    {
        var dialog = new PermissionDialogView();
        using var cts = new CancellationTokenSource();
        var task = dialog.ShowWithDecisionAsync("Read", "读取文件", cts.Token);

        InvokeDecision(dialog, "OnAllow");
        var decision = await task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(PermissionConfirmAction.Allow, decision);
    }

    [Fact]
    public async Task ShowAlwaysAsync_DenyButton_CompletesWithDeny()
    {
        var dialog = new PermissionDialogView();
        using var cts = new CancellationTokenSource();
        var task = dialog.ShowWithDecisionAsync("Write", "写入文件", cts.Token);

        InvokeDecision(dialog, "OnDeny");
        var decision = await task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(PermissionConfirmAction.Deny, decision);
    }

    private static void InvokeDecision(PermissionDialogView dialog, string methodName)
    {
        typeof(PermissionDialogView).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [dialog, EventArgs.Empty]);
    }
}
