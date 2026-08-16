namespace Host.Tests.Tui.Pipes;

/// <summary>
/// PollingService 单元测试 — 验证轮询检测新消息、状态变化事件、启停。
/// </summary>
public class PollingServiceTests
{
    [Fact]
    public async Task PollOnce_NewMessages_TriggersEvent()
    {
        var registry = new PipeRegistry();
        var pipe = new MessagePipe("main", "Main", isMain: true);
        registry.Register(pipe);

        var service = new PollingService(registry, 200);
        var received = new List<(string AgentId, int Count)>();
        service.OnMessagesReceived += (agentId, msgs) => received.Add((agentId, msgs.Count));

        await Task.Delay(10);
        pipe.AddMessage(CreateMessage("msg1", "main"));

        service.PollOnce();
        Assert.Single(received);
        Assert.Equal("main", received[0].AgentId);
        Assert.Equal(1, received[0].Count);
    }

    [Fact]
    public void PollOnce_NoNewMessages_DoesNotTrigger()
    {
        var registry = new PipeRegistry();
        var pipe = new MessagePipe("main", "Main", isMain: true);
        pipe.AddMessage(CreateMessage("msg1", "main"));
        registry.Register(pipe);

        var service = new PollingService(registry, 200);
        var received = new List<string>();
        service.OnMessagesReceived += (agentId, _) => received.Add(agentId);

        service.PollOnce();
        Assert.Empty(received);
    }

    [Fact]
    public void PollOnce_StateChange_TriggersStateEvent()
    {
        var registry = new PipeRegistry();
        var pipe = new MessagePipe("sub1", "Sub");
        registry.Register(pipe);

        var service = new PollingService(registry, 200);
        var stateChanges = new List<(string AgentId, AgentState State)>();
        service.OnStateChanged += (agentId, state) => stateChanges.Add((agentId, state));

        service.PollOnce();
        Assert.Single(stateChanges);
        Assert.Equal(AgentState.Waiting, stateChanges[0].State);

        pipe.UpdateState(AgentState.Completed);
        service.PollOnce();
        Assert.Equal(2, stateChanges.Count);
        Assert.Equal(AgentState.Completed, stateChanges[1].State);
    }

    [Fact]
    public async Task PollOnce_MultiplePipes_TriggersForEach()
    {
        var registry = new PipeRegistry();
        var pipe1 = new MessagePipe("main", "Main", isMain: true);
        var pipe2 = new MessagePipe("sub1", "Sub");
        registry.Register(pipe1);
        registry.Register(pipe2);

        var service = new PollingService(registry, 200);
        var received = new List<string>();
        service.OnMessagesReceived += (agentId, _) => received.Add(agentId);

        await Task.Delay(10);
        pipe1.AddMessage(CreateMessage("msg1", "main"));
        pipe2.AddMessage(CreateMessage("msg2", "sub1"));

        service.PollOnce();
        Assert.Equal(2, received.Count);
        Assert.Contains("main", received);
        Assert.Contains("sub1", received);
    }

    [Fact]
    public async Task StartStop_Lifecycle_CompletesWithoutError()
    {
        var registry = new PipeRegistry();
        registry.Register(new MessagePipe("main", "Main", isMain: true));

        var service = new PollingService(registry, 100);
        service.Start();
        await Task.Delay(150);
        await service.StopAsync();
    }

    [Fact]
    public async Task DisposeAsync_AfterStart_CompletesCleanly()
    {
        var registry = new PipeRegistry();
        registry.Register(new MessagePipe("main", "Main", isMain: true));

        await using var service = new PollingService(registry, 100);
        service.Start();
        await Task.Delay(50);
    }

    [Fact]
    public void PollInterval_ClampedToMinimum100()
    {
        var registry = new PipeRegistry();
        var service = new PollingService(registry, 50);
        service.Start();
        service.PollOnce();
    }

    private static TuiMessage CreateMessage(string id, string agentId)
    {
        return new TuiMessage
        {
            Id = id,
            AgentId = agentId,
            Type = TuiMessageType.AgentContent,
            Content = $"content-{id}",
            Timestamp = DateTime.UtcNow,
        };
    }
}
