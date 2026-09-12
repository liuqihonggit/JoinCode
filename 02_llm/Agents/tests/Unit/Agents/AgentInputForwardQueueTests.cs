namespace Core.Agents.Tests.Unit.Agents;

/// <summary>
/// AgentInputForwardQueue 单元测试 — 验证用户输入转发队列的核心行为
/// </summary>
public class AgentInputForwardQueueTests
{
    [Fact]
    public void TryDrain_UnregisteredAgent_ReturnsEmpty()
    {
        var queue = new AgentInputForwardQueue();
        var result = queue.TryDrain("agent-nonexistent");
        Assert.Empty(result);
    }

    [Fact]
    public void TryDrain_EmptyQueue_ReturnsEmpty()
    {
        var queue = new AgentInputForwardQueue();
        queue.Register("agent-1");
        var result = queue.TryDrain("agent-1");
        Assert.Empty(result);
    }

    [Fact]
    public async Task EnqueueThenTryDrain_ReturnsEnqueuedMessages()
    {
        var queue = new AgentInputForwardQueue();
        queue.Register("agent-1");

        await queue.EnqueueAsync("agent-1", "hello");
        await queue.EnqueueAsync("agent-1", "world");

        var result = queue.TryDrain("agent-1");
        Assert.Equal(2, result.Count);
        Assert.Equal("hello", result[0]);
        Assert.Equal("world", result[1]);
    }

    [Fact]
    public async Task TryDrain_ClearsQueue_SubsequentDrainReturnsEmpty()
    {
        var queue = new AgentInputForwardQueue();
        queue.Register("agent-1");

        await queue.EnqueueAsync("agent-1", "message-1");
        queue.TryDrain("agent-1");

        var secondDrain = queue.TryDrain("agent-1");
        Assert.Empty(secondDrain);
    }

    [Fact]
    public async Task HasPending_ReflectsQueueState()
    {
        var queue = new AgentInputForwardQueue();
        queue.Register("agent-1");

        Assert.False(queue.HasPending("agent-1"));

        await queue.EnqueueAsync("agent-1", "pending-msg");
        Assert.True(queue.HasPending("agent-1"));

        queue.TryDrain("agent-1");
        Assert.False(queue.HasPending("agent-1"));
    }

    [Fact]
    public async Task Unregister_CompletesChannel_SubsequentEnqueueDoesNotThrow()
    {
        var queue = new AgentInputForwardQueue();
        queue.Register("agent-1");
        await queue.EnqueueAsync("agent-1", "before-unregister");

        queue.Unregister("agent-1");

        await queue.EnqueueAsync("agent-1", "after-unregister");
        Assert.False(queue.HasPending("agent-1"));
    }

    [Fact]
    public async Task MultipleAgents_HaveIndependentQueues()
    {
        var queue = new AgentInputForwardQueue();
        queue.Register("agent-a");
        queue.Register("agent-b");

        await queue.EnqueueAsync("agent-a", "msg-for-a");
        await queue.EnqueueAsync("agent-b", "msg-for-b");

        var drainA = queue.TryDrain("agent-a");
        var drainB = queue.TryDrain("agent-b");

        Assert.Single(drainA);
        Assert.Equal("msg-for-a", drainA[0]);
        Assert.Single(drainB);
        Assert.Equal("msg-for-b", drainB[0]);
    }

    [Fact]
    public async Task EnqueueAsync_NullOrWhitespaceInput_Throws()
    {
        var queue = new AgentInputForwardQueue();
        queue.Register("agent-1");

        await Assert.ThrowsAsync<ArgumentException>(() => queue.EnqueueAsync("agent-1", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => queue.EnqueueAsync("agent-1", "   "));
    }

    [Fact]
    public void Register_NullOrWhitespaceAgentId_Throws()
    {
        var queue = new AgentInputForwardQueue();
        Assert.Throws<ArgumentException>(() => queue.Register(""));
        Assert.Throws<ArgumentException>(() => queue.Register("   "));
    }
}
