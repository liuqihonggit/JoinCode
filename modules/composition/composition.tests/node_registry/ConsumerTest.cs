namespace JoinCode.Composition.NodeRegistry.Tests;

/// <summary>
/// Consumer 类单元测试 — 验证 4 个 INodeConsumer 实现的正确性
/// </summary>
public sealed class ConsumerTest {
    [Theory]
    [InlineData("chat-pipeline")]
    [InlineData("tool-execution-pipeline")]
    [InlineData("agent-dispose-pipeline")]
    public void PipelineConsumer_ConsumerId_MatchesConstructor(string pipelineId) {
        var consumer = new PipelineConsumer(pipelineId);
        consumer.ConsumerId.Should().Be(pipelineId);
    }

    [Fact]
    public void PipelineConsumer_EmptyId_ThrowsArgumentException() {
        var act = () => new PipelineConsumer("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToolDispatchConsumer_DefaultConsumerId() {
        var consumer = new ToolDispatchConsumer();
        consumer.ConsumerId.Should().Be("tool-dispatch");
    }

    [Fact]
    public void ToolDispatchConsumer_CustomConsumerId() {
        var consumer = new ToolDispatchConsumer("custom-tool-dispatch");
        consumer.ConsumerId.Should().Be("custom-tool-dispatch");
    }

    [Fact]
    public void CommandDispatchConsumer_DefaultConsumerId() {
        var consumer = new CommandDispatchConsumer();
        consumer.ConsumerId.Should().Be("command-dispatch");
    }

    [Fact]
    public void CommandDispatchConsumer_CustomConsumerId() {
        var consumer = new CommandDispatchConsumer("custom-command-dispatch");
        consumer.ConsumerId.Should().Be("custom-command-dispatch");
    }

    [Fact]
    public void PromptAssemblyConsumer_DefaultConsumerId() {
        var consumer = new PromptAssemblyConsumer();
        consumer.ConsumerId.Should().Be("prompt-assembly");
    }

    [Fact]
    public void PromptAssemblyConsumer_CustomConsumerId() {
        var consumer = new PromptAssemblyConsumer("custom-prompt-assembly");
        consumer.ConsumerId.Should().Be("custom-prompt-assembly");
    }

    [Fact]
    public async Task PipelineConsumer_ResolveNodesAsync_DelegatesToRegistry() {
        var consumer = new PipelineConsumer("chat-pipeline");
        var profile = MakeResolvedProfile("base", ("chat-pipeline", ["node-a", "node-b"]));
        var registry = MakeRegistry("node-a", "node-b");

        var nodes = await consumer.ResolveNodesAsync(registry, profile);

        nodes.Should().HaveCount(2);
        nodes.Select(n => n.NodeId).Should().BeEquivalentTo(["node-a", "node-b"]);
    }

    [Fact]
    public async Task ToolDispatchConsumer_ResolveNodesAsync_DelegatesToRegistry() {
        var consumer = new ToolDispatchConsumer();
        var profile = MakeResolvedProfile("base", ("tool-dispatch", ["shell", "file"]));
        var registry = MakeRegistry("shell", "file");

        var nodes = await consumer.ResolveNodesAsync(registry, profile);

        nodes.Should().HaveCount(2);
        nodes.Select(n => n.NodeId).Should().BeEquivalentTo(["shell", "file"]);
    }

    [Fact]
    public async Task CommandDispatchConsumer_ResolveNodesAsync_DelegatesToRegistry() {
        var consumer = new CommandDispatchConsumer();
        var profile = MakeResolvedProfile("base", ("command-dispatch", ["cmd-a"]));
        var registry = MakeRegistry(["cmd-a"]);

        var nodes = await consumer.ResolveNodesAsync(registry, profile);

        nodes.Should().HaveCount(1);
        nodes[0].NodeId.Should().Be("cmd-a");
    }

    [Fact]
    public async Task PromptAssemblyConsumer_ResolveNodesAsync_DelegatesToRegistry() {
        var consumer = new PromptAssemblyConsumer();
        var profile = MakeResolvedProfile("base", ("prompt-assembly", ["prompt-a", "prompt-b"]));
        var registry = MakeRegistry("prompt-a", "prompt-b");

        var nodes = await consumer.ResolveNodesAsync(registry, profile);

        nodes.Should().HaveCount(2);
        nodes.Select(n => n.NodeId).Should().BeEquivalentTo(["prompt-a", "prompt-b"]);
    }

    // ═══════════════════════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════════════════════

    private static ResolvedProfile MakeResolvedProfile(string name, params (string ConsumerId, string[] NodeIds)[] consumers) {
        var consumerDict = consumers.ToFrozenDictionary(
            c => c.ConsumerId,
            c => new ResolvedConsumerConfig(c.ConsumerId, c.NodeIds.Select(id => new ResolvedNodeEntry(id, 100, null, FrozenDictionary<string, string>.Empty)).ToList()),
            StringComparer.Ordinal);
        return new ResolvedProfile(name, consumerDict);
    }

    private static INodeRegistry MakeRegistry(params string[] nodeIds) {
        var nodes = new Dictionary<string, NodeEntry>(StringComparer.Ordinal);
        foreach (var id in nodeIds) {
            nodes[id] = new NodeEntry(id, _ => new TestNode(id));
        }
        return new TestNodeRegistry(nodes);
    }

    private sealed class TestNode(string nodeId) : INode {
        public string NodeId { get; } = nodeId;
        public NodeMetadata Metadata { get; } = new("1.0", [], [], [], 0, null);
        public NodeState State { get; } = NodeState.Active;
    }

    private sealed class TestNodeRegistry(Dictionary<string, NodeEntry> nodes) : INodeRegistry {
        private readonly FrozenDictionary<string, NodeEntry> _nodes = nodes.ToFrozenDictionary(StringComparer.Ordinal);

        public bool TryGetNode(string nodeId, [NotNullWhen(true)] out INode? node) {
            if (_nodes.TryGetValue(nodeId, out var entry)) {
                node = entry.CreateNode(new EmptyServiceProvider());
                return true;
            }
            node = null;
            return false;
        }

        public Task<IReadOnlyList<INode>> GetNodesForConsumerAsync(string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default) {
            if (!profile.Consumers.TryGetValue(consumerId, out var consumerConfig)) return Task.FromResult<IReadOnlyList<INode>>([]);
            var result = new List<INode>();
            foreach (var nodeEntry in consumerConfig.Nodes.OrderBy(n => n.Priority)) {
                if (_nodes.TryGetValue(nodeEntry.Id, out var entry))
                    result.Add(entry.CreateNode(new EmptyServiceProvider()));
            }
            return Task.FromResult<IReadOnlyList<INode>>(result);
        }

        public Task<IReadOnlyList<T>> GetServicesForConsumerAsync<T>(string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default) where T : class =>
            throw new NotImplementedException();
    }

    private sealed class EmptyServiceProvider : IServiceProvider {
        public object? GetService(Type serviceType) => null;
    }
}
