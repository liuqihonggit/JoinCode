namespace Tui.Tests;

/// <summary>
/// 队列状态广播链路测试 — 验证 TerminalPainter.NotifyQueueChanged 把队列快照
/// 广播到所有注册组件（含 StatusBarView 的"队列:N"段）。
/// 回归背景：主循环曾只直调 queuedCommands.OnQueueChanged，绕过 painter 广播，
/// 导致状态栏"队列：N"永不更新（B5 死路径）。
/// </summary>
public class QueueBroadcastTests
{
    /// <summary>同步 invoke — 测试中无需 Terminal.Gui MainLoop，直接内联执行</summary>
    private static void SyncInvoke(Action action) => action();

    [Fact]
    public void StatusBarView_OnQueueChanged_ShowsQueueCount()
    {
        var bar = new StatusBarView();
        var cmd = new QueuedCommand("hello", CommandOrigin.User, QueuePriority.Now);
        var snapshot = new QueueSnapshot([cmd], [], []);

        bar.OnQueueChanged(snapshot);

        var tree = ViewTreeSerializer.Serialize(bar.TerminalView);
        Assert.Contains("队列:1", tree);
    }

    [Fact]
    public void NotifyQueueChanged_BroadcastsToAllRegisteredComponents()
    {
        var painter = new TerminalPainter(SyncInvoke);
        var bar = new StatusBarView();
        painter.Register(bar);

        var cmd = new QueuedCommand("task", CommandOrigin.User, QueuePriority.Later);
        painter.NotifyQueueChanged(new QueueSnapshot([], [], [cmd]));

        var tree = ViewTreeSerializer.Serialize(bar.TerminalView);
        Assert.Contains("队列:1", tree);
    }
}
