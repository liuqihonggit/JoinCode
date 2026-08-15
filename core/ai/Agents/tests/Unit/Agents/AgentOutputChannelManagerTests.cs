namespace Core.Agents.Tests.Unit.Agents;

/// <summary>
/// AgentOutputChannelManager 单元测试 — 汇聚 channel + 注册/注销/写入/拉取
/// </summary>
public class AgentOutputChannelManagerTests
{
    private static async Task<List<JoinCode.Abstractions.Interfaces.AgentOutputChunk>> ReadChunksAsync(
        JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager manager,
        int expectedCount,
        CancellationToken ct)
    {
        var chunks = new List<JoinCode.Abstractions.Interfaces.AgentOutputChunk>();
        try
        {
            await foreach (var chunk in manager.ReadAllAsync(ct).ConfigureAwait(false))
            {
                chunks.Add(chunk);
                if (chunks.Count >= expectedCount) break;
            }
        }
        catch (OperationCanceledException) { }
        return chunks;
    }

    [Fact]
    public async Task Write_Then_ReadAllAsync_ReturnsChunk()
    {
        var manager = new Coordinator.Core.Messaging.AgentOutputChannelManager();
        manager.Register("agent-001", "explorer");
        manager.Write("agent-001", "explorer", "hello", JoinCode.Abstractions.Interfaces.AgentOutputChunkType.Text);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var chunks = await ReadChunksAsync(manager, 1, cts.Token);

        Assert.Single(chunks);
        Assert.Equal("agent-001", chunks[0].AgentId);
        Assert.Equal("explorer", chunks[0].AgentName);
        Assert.Equal("hello", chunks[0].Content);
    }

    [Fact]
    public async Task Write_MultipleChunks_AllReturned()
    {
        var manager = new Coordinator.Core.Messaging.AgentOutputChannelManager();
        manager.Register("agent-001", "explorer");
        manager.Write("agent-001", "explorer", "hello", JoinCode.Abstractions.Interfaces.AgentOutputChunkType.Text);
        manager.Write("agent-001", "explorer", " world", JoinCode.Abstractions.Interfaces.AgentOutputChunkType.Text);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var chunks = await ReadChunksAsync(manager, 2, cts.Token);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("hello", chunks[0].Content);
        Assert.Equal(" world", chunks[1].Content);
    }

    [Fact]
    public async Task Write_MultipleAgents_AllReturnedInOrder()
    {
        var manager = new Coordinator.Core.Messaging.AgentOutputChannelManager();
        manager.Register("agent-001", "explorer");
        manager.Register("agent-002", "planner");
        manager.Write("agent-001", "explorer", "exploring", JoinCode.Abstractions.Interfaces.AgentOutputChunkType.Text);
        manager.Write("agent-002", "planner", "planning", JoinCode.Abstractions.Interfaces.AgentOutputChunkType.Text);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var chunks = await ReadChunksAsync(manager, 2, cts.Token);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("agent-001", chunks[0].AgentId);
        Assert.Equal("agent-002", chunks[1].AgentId);
    }

    [Fact]
    public void Write_EmptyContent_Skipped()
    {
        var manager = new Coordinator.Core.Messaging.AgentOutputChannelManager();
        manager.Register("agent-001", "explorer");
        manager.Write("agent-001", "explorer", "", JoinCode.Abstractions.Interfaces.AgentOutputChunkType.Text);

        var agents = manager.GetActiveAgents();
        Assert.Single(agents);
    }

    [Fact]
    public void GetActiveAgents_AfterRegister_ReturnsAgent()
    {
        var manager = new Coordinator.Core.Messaging.AgentOutputChannelManager();
        manager.Register("agent-001", "explorer");
        manager.Register("agent-002", null);

        var agents = manager.GetActiveAgents();
        Assert.Equal(2, agents.Count);
        Assert.Contains(agents, a => a.AgentId == "agent-001" && a.DisplayName == "explorer");
        Assert.Contains(agents, a => a.AgentId == "agent-002" && a.DisplayName == null);
    }

    [Fact]
    public void GetActiveAgents_AfterUnregister_Removed()
    {
        var manager = new Coordinator.Core.Messaging.AgentOutputChannelManager();
        manager.Register("agent-001", "explorer");
        manager.Register("agent-002", "planner");

        manager.Unregister("agent-001");

        var agents = manager.GetActiveAgents();
        Assert.Single(agents);
        Assert.Equal("agent-002", agents[0].AgentId);
    }

    [Fact]
    public void GetActiveAgents_Empty_ReturnsEmptyList()
    {
        var manager = new Coordinator.Core.Messaging.AgentOutputChannelManager();
        Assert.Empty(manager.GetActiveAgents());
    }
}
