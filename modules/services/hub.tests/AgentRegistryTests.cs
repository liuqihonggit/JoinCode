namespace Hub.Tests;

public class AgentRegistryTests {
    [Fact]
    public void Register_AddsAgentToRegistry() {
        var registry = new AgentRegistry();
        registry.Register("agent-1");
        registry.Count.Should().Be(1);
        registry.IsRegistered("agent-1").Should().BeTrue();
    }

    [Fact]
    public void Unregister_RemovesAgentFromRegistry() {
        var registry = new AgentRegistry();
        registry.Register("agent-1");
        registry.Unregister("agent-1");
        registry.Count.Should().Be(0);
        registry.IsRegistered("agent-1").Should().BeFalse();
    }

    [Fact]
    public async Task DeliverMessageAsync_ToRegisteredAgent_ReturnsTrue() {
        var registry = new AgentRegistry();
        registry.Register("agent-1");
        var message = new AgentMessageRequest {
            FromAgentId = "agent-2",
            ToAgentId = "agent-1",
            Content = "hello"
        };

        var result = await registry.DeliverMessageAsync("agent-1", message, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeliverMessageAsync_ToUnregisteredAgent_ReturnsFalse() {
        var registry = new AgentRegistry();
        var message = new AgentMessageRequest {
            FromAgentId = "agent-2",
            ToAgentId = "agent-1",
            Content = "hello"
        };

        var result = await registry.DeliverMessageAsync("agent-1", message, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task BroadcastMessageAsync_DeliversToAllExceptSender() {
        var registry = new AgentRegistry();
        registry.Register("agent-1");
        registry.Register("agent-2");
        registry.Register("agent-3");

        var message = new AgentMessageRequest {
            FromAgentId = "agent-1",
            Content = "broadcast"
        };

        await registry.BroadcastMessageAsync("agent-1", message, CancellationToken.None);

        var msg2 = await registry.ReceiveAsync("agent-2", CancellationToken.None).FirstAsync();
        var msg3 = await registry.ReceiveAsync("agent-3", CancellationToken.None).FirstAsync();

        msg2.Content.Should().Be("broadcast");
        msg3.Content.Should().Be("broadcast");
    }

    [Fact]
    public async Task ReceiveAsync_ReturnsMessagesInOrder() {
        var registry = new AgentRegistry();
        registry.Register("agent-1");

        await registry.DeliverMessageAsync("agent-1", new AgentMessageRequest {
            FromAgentId = "agent-2",
            ToAgentId = "agent-1",
            Content = "first"
        }, CancellationToken.None);

        await registry.DeliverMessageAsync("agent-1", new AgentMessageRequest {
            FromAgentId = "agent-2",
            ToAgentId = "agent-1",
            Content = "second"
        }, CancellationToken.None);

        var messages = new List<AgentMessageRequest>();
        await foreach (var msg in registry.ReceiveAsync("agent-1", CancellationToken.None)) {
            messages.Add(msg);
            if (messages.Count >= 2) break;
        }

        messages[0].Content.Should().Be("first");
        messages[1].Content.Should().Be("second");
    }

    [Fact]
    public void GetAll_ReturnsAllRegisteredAgents() {
        var registry = new AgentRegistry();
        registry.Register("agent-1", "session-1");
        registry.Register("agent-2");

        var all = registry.GetAll();

        all.Should().HaveCount(2);
        all.Should().Contain(a => a.AgentId == "agent-1" && a.SessionId == "session-1");
        all.Should().Contain(a => a.AgentId == "agent-2");
    }
}
