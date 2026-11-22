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

    [Fact]
    public void Register_WithCapabilities_StoresCapabilities() {
        var registry = new AgentRegistry();
        registry.Register("agent-1", capabilities: ["code-review", "testing"]);

        var all = registry.GetAll();
        all.Should().HaveCount(1);
        all[0].Capabilities.Should().Contain("code-review");
        all[0].Capabilities.Should().Contain("testing");
    }

    [Fact]
    public async Task RouteByCapabilityAsync_WithMatchingAgent_RoutesToAgent() {
        var registry = new AgentRegistry();
        registry.Register("agent-1", capabilities: ["code-review"]);
        registry.Register("agent-2", capabilities: ["testing"]);

        var message = new AgentMessageRequest {
            FromAgentId = "orchestrator",
            Content = "review this code"
        };

        var targetId = await registry.RouteByCapabilityAsync("code-review", message, CancellationToken.None);

        targetId.Should().Be("agent-1");
    }

    [Fact]
    public async Task RouteByCapabilityAsync_NoMatchingAgent_ReturnsNull() {
        var registry = new AgentRegistry();
        registry.Register("agent-1", capabilities: ["code-review"]);

        var message = new AgentMessageRequest {
            FromAgentId = "orchestrator",
            Content = "deploy this"
        };

        var targetId = await registry.RouteByCapabilityAsync("deployment", message, CancellationToken.None);

        targetId.Should().BeNull();
    }

    [Fact]
    public async Task RouteByCapabilityAsync_LoadBalancing_SelectsLeastBusy() {
        var registry = new AgentRegistry();
        registry.Register("agent-1", capabilities: ["code-review"]);
        registry.Register("agent-2", capabilities: ["code-review"]);
        registry.Register("agent-3", capabilities: ["code-review"]);

        await registry.DeliverMessageAsync("agent-1", new AgentMessageRequest {
            FromAgentId = "orchestrator",
            Content = "task 1"
        }, CancellationToken.None);
        await registry.DeliverMessageAsync("agent-1", new AgentMessageRequest {
            FromAgentId = "orchestrator",
            Content = "task 2"
        }, CancellationToken.None);
        await registry.DeliverMessageAsync("agent-2", new AgentMessageRequest {
            FromAgentId = "orchestrator",
            Content = "task 3"
        }, CancellationToken.None);

        var targetId = await registry.RouteByCapabilityAsync("code-review",
            new AgentMessageRequest { FromAgentId = "orchestrator", Content = "task 4" },
            CancellationToken.None);

        targetId.Should().Be("agent-3");
    }

    [Fact]
    public async Task DeliverMessageAsync_IncrementsPendingMessages() {
        var registry = new AgentRegistry();
        registry.Register("agent-1");

        await registry.DeliverMessageAsync("agent-1", new AgentMessageRequest {
            FromAgentId = "agent-2",
            Content = "hello"
        }, CancellationToken.None);

        var all = registry.GetAll();
        all[0].PendingMessages.Should().Be(1);
    }

    [Fact]
    public async Task BroadcastMessageAsync_IncrementsPendingMessages() {
        var registry = new AgentRegistry();
        registry.Register("agent-1");
        registry.Register("agent-2");

        await registry.BroadcastMessageAsync("agent-1", new AgentMessageRequest {
            FromAgentId = "agent-1",
            Content = "broadcast"
        }, CancellationToken.None);

        var all = registry.GetAll();
        var agent2 = all.First(a => a.AgentId == "agent-2");
        agent2.PendingMessages.Should().Be(1);
    }

    [Fact]
    public async Task RouteByCapabilityAsync_DeliversMessageToTarget() {
        var registry = new AgentRegistry();
        registry.Register("agent-1", capabilities: ["code-review"]);

        var targetId = await registry.RouteByCapabilityAsync("code-review",
            new AgentMessageRequest { FromAgentId = "orchestrator", Content = "review this" },
            CancellationToken.None);

        targetId.Should().Be("agent-1");
        var all = registry.GetAll();
        all[0].PendingMessages.Should().Be(1);
    }

    [Fact]
    public void UpdateHeartbeat_UpdatesLastHeartbeatAt() {
        var registry = new AgentRegistry();
        registry.Register("agent-1");
        var beforeHeartbeat = registry.GetAll()[0].LastHeartbeatAt;

        var result = registry.UpdateHeartbeat("agent-1");

        result.Should().BeTrue();
        var afterHeartbeat = registry.GetAll()[0].LastHeartbeatAt;
        afterHeartbeat.Should().BeOnOrAfter(beforeHeartbeat);
    }

    [Fact]
    public void UpdateHeartbeat_UnregisteredAgent_ReturnsFalse() {
        var registry = new AgentRegistry();

        var result = registry.UpdateHeartbeat("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Register_SetsLastHeartbeatAtToConnectedAt() {
        var registry = new AgentRegistry();
        registry.Register("agent-1");

        var all = registry.GetAll();
        all[0].LastHeartbeatAt.Should().BeCloseTo(all[0].ConnectedAt, TimeSpan.FromSeconds(1));
        await Task.CompletedTask;
    }
}
