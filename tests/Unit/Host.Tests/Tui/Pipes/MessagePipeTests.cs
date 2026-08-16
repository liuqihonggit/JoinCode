namespace Host.Tests.Tui.Pipes;

/// <summary>
/// MessagePipe 单元测试 — 验证消息添加、增量拉取、状态更新、清空、上限截断。
/// </summary>
public class MessagePipeTests
{
    [Fact]
    public void Constructor_MainAgent_StateIsRunning()
    {
        var pipe = new MessagePipe("main", "Main Agent", isMain: true);
        Assert.Equal(AgentState.Running, pipe.State);
        Assert.True(pipe.IsMain);
    }

    [Fact]
    public void Constructor_SubAgent_StateIsWaiting()
    {
        var pipe = new MessagePipe("sub1", "Sub Agent", isMain: false);
        Assert.Equal(AgentState.Waiting, pipe.State);
        Assert.False(pipe.IsMain);
    }

    [Fact]
    public void AddMessage_IncreasesCount()
    {
        var pipe = new MessagePipe("main", "Main");
        pipe.AddMessage(CreateMessage("msg1", "main"));
        Assert.Equal(1, pipe.MessageCount);
    }

    [Fact]
    public void GetNewMessages_FiltersByTimestamp()
    {
        var pipe = new MessagePipe("main", "Main");
        var before = DateTime.UtcNow.AddSeconds(-1);
        pipe.AddMessage(CreateMessage("msg1", "main", before));
        var after = DateTime.UtcNow;
        pipe.AddMessage(CreateMessage("msg2", "main", after.AddSeconds(1)));

        var newMsgs = pipe.GetNewMessages(after);
        Assert.Single(newMsgs);
        Assert.Equal("msg2", newMsgs[0].Id);
    }

    [Fact]
    public void GetNewMessages_EmptyPipe_ReturnsEmpty()
    {
        var pipe = new MessagePipe("main", "Main");
        var result = pipe.GetNewMessages(DateTime.MinValue);
        Assert.Empty(result);
    }

    [Fact]
    public void UpdateState_ChangesState()
    {
        var pipe = new MessagePipe("sub1", "Sub");
        Assert.Equal(AgentState.Waiting, pipe.State);

        pipe.UpdateState(AgentState.Running);
        Assert.Equal(AgentState.Running, pipe.State);

        pipe.UpdateState(AgentState.Completed);
        Assert.Equal(AgentState.Completed, pipe.State);
    }

    [Fact]
    public void Clear_RemovesAllMessages()
    {
        var pipe = new MessagePipe("main", "Main");
        pipe.AddMessage(CreateMessage("msg1", "main"));
        pipe.AddMessage(CreateMessage("msg2", "main"));
        Assert.Equal(2, pipe.MessageCount);

        pipe.Clear();
        Assert.Equal(0, pipe.MessageCount);
    }

    [Fact]
    public void AddMessage_ExceedsMax_TrimsOldest()
    {
        var pipe = new MessagePipe("main", "Main");
        for (var i = 0; i < 1005; i++)
        {
            pipe.AddMessage(CreateMessage($"msg{i}", "main"));
        }
        Assert.Equal(1000, pipe.MessageCount);
    }

    [Fact]
    public void Messages_ReturnsReadOnlySnapshot()
    {
        var pipe = new MessagePipe("main", "Main");
        pipe.AddMessage(CreateMessage("msg1", "main"));
        var snapshot = pipe.Messages;
        pipe.AddMessage(CreateMessage("msg2", "main"));
        Assert.Single(snapshot);
    }

    private static TuiMessage CreateMessage(string id, string agentId, DateTime? timestamp = null)
    {
        return new TuiMessage
        {
            Id = id,
            AgentId = agentId,
            Type = TuiMessageType.AgentContent,
            Content = $"content-{id}",
            Timestamp = timestamp ?? DateTime.UtcNow,
        };
    }
}
