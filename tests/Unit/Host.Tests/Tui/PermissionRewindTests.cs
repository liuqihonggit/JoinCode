namespace Host.Tests.Tui;

/// <summary>
/// 权限重试历史裁剪测试 — 验证 RewindToSnapshot 把 chatHistory 裁剪回命令执行前快照。
/// 回归背景（B7）：QueryEngine.QueryAsync 在管道执行前就 AddUserMessage，权限异常抛出时
/// 用户消息+部分回复已入历史；TUI 批准后重发原文会二次追加导致上下文重复。
/// 修复对齐 GUI 的 RewindLastTurnAsync 语义：批准后先裁剪再重发。
/// </summary>
public class PermissionRewindTests
{
    [Fact]
    public void RewindToSnapshot_TrimsMessagesAddedAfterSnapshot()
    {
        var history = new MessageList();
        history.AddSystemMessage("system");
        var snapshot = history.Count;
        history.AddUserMessage("用户消息");
        history.AddAssistantMessage("部分回复");
        history.AddToolMessage("工具残留");
        Assert.Equal(4, history.Count);

        TuiModeRunner.RewindToSnapshot(history, snapshot);

        Assert.Equal(snapshot, history.Count);
        Assert.Equal("system", history[^1].Content);
    }

    [Fact]
    public void RewindToSnapshot_EmptyDelta_NoOp()
    {
        var history = new MessageList();
        history.AddUserMessage("已有消息");
        var snapshot = history.Count;

        TuiModeRunner.RewindToSnapshot(history, snapshot);

        Assert.Single(history);
        Assert.Equal("已有消息", history[^1].Content);
    }
}
