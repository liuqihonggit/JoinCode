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
            nodes[id] = new NodeEntry(id, (_, _) => new TestNode(id));
        }
        return new TestNodeRegistry(nodes);
    }

    [Fact]
    public async Task NodeRegistry_InjectsProfileMetadataIntoRuntimeNode() {
        // G4 真实注入链（非死路径）：profile 的 meta.* → ResolvedNodeEntry.Config → INode.Metadata。
        // 走生产 GetNodesForConsumerAsync，断言运行期节点真的拿到配置值。
        var config = new Dictionary<string, string> {
            [NodeMetadata.ConfigPrefix + "version"] = "2.1.0",
            [NodeMetadata.ConfigPrefix + "capabilities"] = "rate-limit,audit"
        }.ToFrozenDictionary();
        var profile = new ResolvedProfile("test", new Dictionary<string, ResolvedConsumerConfig> {
            ["pipeline"] = new("pipeline", [
                new ResolvedNodeEntry("injected-node", 100, null, config)
            ])
        }.ToFrozenDictionary());
        var registry = NodeRegistry.Build(new EmptyServiceProvider(), [
            new KeyValuePair<string, Func<IServiceProvider, NodeMetadata?, INode>>(
                "injected-node", (_, meta) => new Node<object>("injected-node", new object(), meta))
        ]);

        var nodes = await registry.GetNodesForConsumerAsync("pipeline", profile);

        var node = Assert.Single(nodes);
        node.Metadata.Version.Should().Be("2.1.0");
        node.Metadata.Capabilities.Should().Equal("rate-limit", "audit");
        node.Metadata.Priority.Should().Be(100);       // 路由字段来自 ResolvedNodeEntry，非 meta.*
    }

    [Fact]
    public async Task NodeRegistry_BuildFromCatalog_AutoRegistersAllNodes() {
        // G5: 节点从编译期工厂 catalog 自动归纳，不再手工登记。
        // 完整 consumerId:nodeId 是注册键 —— 同名节点在不同消费者不互相覆盖（P0 回归）。
        // 已是 INode 的直接用；普通服务包装成 Node<T>（G4 的 metadata 注入链因此真正生效）。
        var catalog = new[] {
            new KeyValuePair<string, Func<IServiceProvider, object>>("pipeline-a:plain", _ => new object()),
            new KeyValuePair<string, Func<IServiceProvider, object>>("pipeline-b:typed", _ => new TestNode("typed")),
        };
        var profile = new ResolvedProfile("test", new Dictionary<string, ResolvedConsumerConfig> {
            ["pipeline-a"] = new("pipeline-a", [
                new ResolvedNodeEntry("plain", 0, null, FrozenDictionary<string, string>.Empty)]),
            ["pipeline-b"] = new("pipeline-b", [
                new ResolvedNodeEntry("typed", 0, null, FrozenDictionary<string, string>.Empty)])
        }.ToFrozenDictionary());

        var registry = NodeRegistry.BuildFromCatalog(new EmptyServiceProvider(), catalog);

        Assert.Equal(2, registry.NodeCount);
        Assert.IsType<Node<object>>(Assert.Single(await registry.GetNodesForConsumerAsync("pipeline-a", profile)));
        Assert.IsType<TestNode>(Assert.Single(await registry.GetNodesForConsumerAsync("pipeline-b", profile)));
    }

    // ===== G1 可逆注册（DSH ctx.effect 语义：注册→替换→卸载）=====

    [Fact]
    public void NodeRegistry_RegisterReturnsHandle_DisposeRollsBackInReverseOrder() {
        var registry = NodeRegistry.Build(new EmptyServiceProvider());
        Assert.False(registry.TryGetNode("x", out _));

        var first = registry.Register("x", (_, _) => new TestNode("first"));
        Assert.True(registry.TryGetNode("x", out var n1));
        Assert.NotNull(n1);
        Assert.Equal("first", n1.NodeId);

        var second = registry.Register("x", (_, _) => new TestNode("second"));
        Assert.True(registry.TryGetNode("x", out var n2));
        Assert.NotNull(n2);
        Assert.Equal("second", n2.NodeId);

        // 逆序回滚：后注册的先 Dispose，恢复前一个工厂（不是简单清空）。
        second.Dispose();
        Assert.True(registry.TryGetNode("x", out var n3));
        Assert.NotNull(n3);
        Assert.Equal("first", n3.NodeId);

        first.Dispose();
        Assert.False(registry.TryGetNode("x", out _));
    }

    [Fact]
    public void NodeRegistry_HandleReplace_UpdatesFactoryInPlace() {
        var registry = NodeRegistry.Build(new EmptyServiceProvider());
        using var handle = registry.Register("y", (_, _) => new TestNode("original"));

        handle.Replace((_, _) => new TestNode("replaced"));
        Assert.True(registry.TryGetNode("y", out var node));
        Assert.NotNull(node);
        Assert.Equal("replaced", node.NodeId);

        // Replace 幂等于就地换工厂：Dispose 后节点应消失，不留副本。
        handle.Dispose();
        Assert.False(registry.TryGetNode("y", out _));
    }

    [Fact]
    public async Task NodeRegistry_DynamicallyRegisteredNode_IsConsumedByProfile() {
        // 动态注册的 consumer 专属节点（完整 key）必须能被 GetNodesForConsumerAsync 消费。
        var registry = NodeRegistry.Build(new EmptyServiceProvider());
        using var handle = registry.Register("probe-consumer:probe", (_, _) => new TestNode("dynamic"));
        var profile = new ResolvedProfile("probe", new Dictionary<string, ResolvedConsumerConfig> {
            ["probe-consumer"] = new("probe-consumer", [
                new ResolvedNodeEntry("probe", 0, null, FrozenDictionary<string, string>.Empty)
            ])
        }.ToFrozenDictionary());

        var nodes = await registry.GetNodesForConsumerAsync("probe-consumer", profile);

        var node = Assert.Single(nodes);
        Assert.Equal("dynamic", node.NodeId);
        handle.Dispose();
        Assert.Empty(await registry.GetNodesForConsumerAsync("probe-consumer", profile));
    }

    private sealed class TestNode(string nodeId) : INode {
        public string NodeId { get; } = nodeId;
        public NodeMetadata Metadata { get; } = NodeMetadata.Unspecified;
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

        public RegistrationHandle Register(string nodeId, Func<IServiceProvider, NodeMetadata?, INode> factory) =>
            throw new NotSupportedException("测试替身不支持运行期注册");

        public Task<IReadOnlyList<T>> GetServicesForConsumerAsync<T>(
            string consumerId,
            ResolvedProfile profile,
            CancellationToken cancellationToken = default,
            bool failFastOnMissing = false) where T : class =>
            throw new NotImplementedException();
    }

    private sealed class EmptyServiceProvider : IServiceProvider {
        public object? GetService(Type serviceType) => null;
    }
}
