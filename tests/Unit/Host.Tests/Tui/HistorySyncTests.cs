namespace Host.Tests.Tui;

/// <summary>
/// 斜杠命令后历史同步测试 — 验证 SyncHistoryFromEngine 把引擎消息记录重建进 TUI chatHistory。
/// 回归背景（T1）：/resume 在引擎层装入历史、/clear 清空上下文，但 TUI 本地 chatHistory 不刷新，
/// 导致后续对话上下文与引擎不一致（恢复的会话对模型不可见）。
/// </summary>
public class HistorySyncTests
{
    [Fact]
    public void SyncHistoryFromEngine_MapsRolesAndContents_ReplacesHistory()
    {
        var history = new MessageList();
        history.AddUserMessage("旧内容");

        var records = new List<ApiMessageRecord>
        {
            new() { Role = "system", Content = "系统提示" },
            new() { Role = "user", Content = "第一问" },
            new() { Role = "assistant", Content = "第一答" },
            new() { Role = "tool", Content = "工具结果" },
        };

        TuiModeRunner.SyncHistoryFromEngine(history, records);

        Assert.Equal(4, history.Count);
        Assert.Equal(MessageRole.System, history[0].Role);
        Assert.Equal("系统提示", history[0].Content);
        Assert.Equal(MessageRole.User, history[1].Role);
        Assert.Equal("第一问", history[1].Content);
        Assert.Equal(MessageRole.Assistant, history[2].Role);
        Assert.Equal("第一答", history[2].Content);
        Assert.Equal(MessageRole.Tool, history[3].Role);
    }

    [Fact]
    public void SyncHistoryFromEngine_EmptyRecords_ClearsHistory()
    {
        // /clear 后引擎返回空列表 — chatHistory 必须同步清空
        var history = new MessageList();
        history.AddUserMessage("将被清空");
        history.AddAssistantMessage("同上");

        TuiModeRunner.SyncHistoryFromEngine(history, []);

        Assert.Empty(history);
    }

    [Fact]
    public void SyncHistoryFromEngine_UnknownRole_FallsBackToTool()
    {
        var history = new MessageList();

        TuiModeRunner.SyncHistoryFromEngine(
            history, [new ApiMessageRecord { Role = "exotic-role", Content = "未知角色" }]);

        var message = Assert.Single(history);
        Assert.Equal("未知角色", message.Content);
    }
}
