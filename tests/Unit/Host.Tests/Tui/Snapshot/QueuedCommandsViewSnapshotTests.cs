namespace Host.Tests.Tui.Snapshot;

/// <summary>
/// QueuedCommandsView 快照测试 — 验证投递预览组件的显示/隐藏行为。
/// P0-3 组件接入：队列有内容时可见，空时隐藏。
/// </summary>
public class QueuedCommandsViewSnapshotTests
{
    [Fact]
    public void EmptyQueue_Hidden()
    {
        var queue = new CommandQueue();
        var view = new QueuedCommandsView(queue);
        view.OnQueueChanged(queue.GetSnapshot());

        var actual = ViewTreeSerializer.Serialize(view.TerminalView);
        SnapshotVerifier.Verify(actual, "QueuedCommands_Empty");
    }

    [Fact]
    public void WithPendingCommands_VisibleWithItems()
    {
        var queue = new CommandQueue();
        queue.Enqueue(new QueuedCommand("hello", CommandOrigin.User, QueuePriority.Next));
        queue.Enqueue(new QueuedCommand("world", CommandOrigin.User, QueuePriority.Later));
        var view = new QueuedCommandsView(queue);
        view.OnResize(80, 24);
        view.OnQueueChanged(queue.GetSnapshot());

        var actual = ViewTreeSerializer.Serialize(view.TerminalView);
        SnapshotVerifier.Verify(actual, "QueuedCommands_WithItems");
    }
}
