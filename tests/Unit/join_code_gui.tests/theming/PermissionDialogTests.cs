namespace JoinCode.Gui.Tests.Theming;

/// <summary>
/// 权限确认弹窗冒烟测试 — Headless 渲染真实 <see cref="PermissionDialog"/>，
/// 验证：① 弹窗能正常显示且包含三枚决策按钮；② 点击按钮返回对应决策；
/// ③ 关闭窗口等价于拒绝。覆盖 View 层弹窗闭环接线，避免仅"编译过但运行时崩"。
/// </summary>
public sealed class PermissionDialogTests
{
    /// <summary>展示弹窗并返回 Task，用于等待 ShowDialog 完成</summary>
    private static async Task<PermissionConfirmationDecision> ShowDialogAndClickAsync(string buttonText)
    {
        var host = new Window { Width = 200, Height = 200 };
        host.Show();
        try
        {
            var request = new PermissionConfirmationRequest("bash", "运行命令 echo hi？", "req-smoke", "rule-content");
            var dialog = new PermissionDialog(request);
            var resultTask = dialog.ShowDialog<PermissionConfirmationDecision>(host);

            // 等弹窗挂载后从可视树定位目标按钮
            var button = await FindButtonAsync(dialog, buttonText)
                ?? throw new InvalidOperationException($"未找到按钮: {buttonText}");
            button.Command?.Execute(null);

            return await resultTask;
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>递归查找文本匹配的按钮（Headless 无布局线程，直接遍历可视树）</summary>
    private static async Task<Button?> FindButtonAsync(Window dialog, string text)
    {
        // 通过 RunJobs 泵起 UI 事件循环让 Content 挂载进可视树（Headless 无真实时钟）
        for (int i = 0; i < 50; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var found = dialog.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Content?.ToString() == text);
            if (found is not null)
            {
                return found;
            }
            await Task.Yield();
        }
        return null;
    }

    [AvaloniaFact]
    public void Dialog_ContainsThreeDecisionButtons()
    {
        var request = new PermissionConfirmationRequest("bash", "运行命令 echo hi？", "req-1", "rule");
        var dialog = new PermissionDialog(request);
        dialog.Show();
        try
        {
            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToList();
            buttons.ShouldContainText("拒绝");
            buttons.ShouldContainText("允许本次");
            buttons.ShouldContainText("始终允许");
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClickAllow_ReturnsAllow()
    {
        var decision = await ShowDialogAndClickAsync("允许本次");
        Assert.Equal(PermissionConfirmationDecision.Allow, decision);
    }

    [AvaloniaFact]
    public async Task ClickAlwaysAllow_ReturnsAlwaysAllow()
    {
        var decision = await ShowDialogAndClickAsync("始终允许");
        Assert.Equal(PermissionConfirmationDecision.AlwaysAllow, decision);
    }

    [AvaloniaFact]
    public async Task ClickDeny_ReturnsDeny()
    {
        var decision = await ShowDialogAndClickAsync("拒绝");
        Assert.Equal(PermissionConfirmationDecision.Deny, decision);
    }

    /// <summary>
    /// 关窗兜底契约 — 不点任何按钮直接关闭窗口（模拟用户点标题栏 ✕ / Alt+F4），
    /// ShowDialog 必须返回 Deny（拒绝）。Avalonia 对未设置 _dialogResult 的关窗返回
    /// default(TResult)，该兜底成立的前提是 PermissionConfirmationDecision 枚举首值为
    /// Deny(=0)。若有人调整枚举顺序，本测试立即红灯，防止"关窗=批准"安全漏洞回归。
    /// </summary>
    [AvaloniaFact]
    public async Task CloseWindowWithoutClicking_ReturnsDeny()
    {
        var host = new Window { Width = 200, Height = 200 };
        host.Show();
        try
        {
            var request = new PermissionConfirmationRequest("bash", "运行命令 echo hi？", "req-close", "rule-content");
            var dialog = new PermissionDialog(request);
            var resultTask = dialog.ShowDialog<PermissionConfirmationDecision>(host);

            // 等弹窗挂载完成后直接关闭（不点击任何决策按钮）
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            dialog.Close();

            var decision = await resultTask;
            Assert.Equal(PermissionConfirmationDecision.Deny, decision);
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>
    /// 枚举契约钉死 — Deny 必须是 PermissionConfirmationDecision 的默认值(=0)。
    /// 与上一测试互为双保险：Headless 测试验证运行时行为，本测试在纯枚举层面
    /// 阻断"重排枚举成员导致默认值漂移"的重构事故。
    /// </summary>
    [Fact]
    public void DefaultDecision_MustBeDeny()
    {
        Assert.Equal(PermissionConfirmationDecision.Deny, default(PermissionConfirmationDecision));
    }
}

/// <summary>为测试断言补一个小的断言扩展（避免引入额外断言库依赖）</summary>
internal static class ButtonListAssertExtensions
{
    public static void ShouldContainText(this IEnumerable<Button> buttons, string text)
    {
        Assert.Contains(buttons, b => b.Content?.ToString() == text);
    }
}
