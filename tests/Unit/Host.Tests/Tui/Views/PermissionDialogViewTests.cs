namespace Host.Tests.Tui.Views;

/// <summary>
/// PermissionDialogView 单元测试 — 验证权限弹窗显示/隐藏/取消行为。
/// P0-2 权限闭环：ShowAsync 显示弹窗 → 用户决策 → Hide 隐藏。
/// </summary>
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
}
