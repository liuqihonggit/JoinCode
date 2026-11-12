namespace JoinCode.Composition.NodeRegistry.Tests;

/// <summary>
/// NodeProfileResolver 单元测试 — 验证 Profile 继承链解析（排除/新增/覆写）
/// </summary>
public sealed class NodeProfileResolverTest {
    [Fact]
    public void Resolve_NoInherits_ReturnsSelfConsumers() {
        var baseProfile = MakeProfile("base", consumers: MakeConsumers(
            MakeConsumer("chat-pipeline", MakeNode("node-a", 100), MakeNode("node-b", 200))
        ));
        var resolver = new NodeProfileResolver(MakeProfilesDict(baseProfile));

        var resolved = resolver.Resolve("base");

        resolved.Name.Should().Be("base");
        resolved.Consumers.Should().ContainKey("chat-pipeline");
        resolved.Consumers["chat-pipeline"].Nodes.Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_WithInherits_MergesParentConsumers() {
        var baseProfile = MakeProfile("base", consumers: MakeConsumers(
            MakeConsumer("chat-pipeline", MakeNode("node-a", 100))
        ));
        var childProfile = MakeProfile("child", inherits: "base");
        var resolver = new NodeProfileResolver(MakeProfilesDict(baseProfile, childProfile));

        var resolved = resolver.Resolve("child");

        resolved.Consumers.Should().ContainKey("chat-pipeline");
        resolved.Consumers["chat-pipeline"].Nodes.Should().HaveCount(1);
        resolved.Consumers["chat-pipeline"].Nodes[0].Id.Should().Be("node-a");
    }

    [Fact]
    public void Resolve_Excludes_RemovesNodeFromAllConsumers() {
        var baseProfile = MakeProfile("base", consumers: MakeConsumers(
            MakeConsumer("pipeline-a", MakeNode("crash-snapshot-mw", 50), MakeNode("node-a", 100)),
            MakeConsumer("pipeline-b", MakeNode("crash-snapshot-mw", 50), MakeNode("node-b", 100))
        ));
        var childProfile = MakeProfile("child", inherits: "base", excludes: ["crash-snapshot-mw"]);
        var resolver = new NodeProfileResolver(MakeProfilesDict(baseProfile, childProfile));

        var resolved = resolver.Resolve("child");

        resolved.Consumers["pipeline-a"].Nodes.Should().NotContain(n => n.Id == "crash-snapshot-mw");
        resolved.Consumers["pipeline-b"].Nodes.Should().NotContain(n => n.Id == "crash-snapshot-mw");
        resolved.Consumers["pipeline-a"].Nodes.Should().Contain(n => n.Id == "node-a");
        resolved.Consumers["pipeline-b"].Nodes.Should().Contain(n => n.Id == "node-b");
    }

    [Fact]
    public void Resolve_Includes_AddsNodesToExistingConsumer() {
        var baseProfile = MakeProfile("base", consumers: MakeConsumers(
            MakeConsumer("tool-dispatch", MakeNode("shell-tool", 0))
        ));
        var childProfile = MakeProfile("child", inherits: "base", includes: MakeConsumers(
            MakeConsumer("tool-dispatch", MakeNode("sdk-api-tool", 0))
        ));
        var resolver = new NodeProfileResolver(MakeProfilesDict(baseProfile, childProfile));

        var resolved = resolver.Resolve("child");

        resolved.Consumers["tool-dispatch"].Nodes.Should().HaveCount(2);
        resolved.Consumers["tool-dispatch"].Nodes.Should().Contain(n => n.Id == "shell-tool");
        resolved.Consumers["tool-dispatch"].Nodes.Should().Contain(n => n.Id == "sdk-api-tool");
    }

    [Fact]
    public void Resolve_Includes_CreatesNewConsumer() {
        var baseProfile = MakeProfile("base");
        var childProfile = MakeProfile("child", inherits: "base", includes: MakeConsumers(
            MakeConsumer("new-consumer", MakeNode("new-node", 0))
        ));
        var resolver = new NodeProfileResolver(MakeProfilesDict(baseProfile, childProfile));

        var resolved = resolver.Resolve("child");

        resolved.Consumers.Should().ContainKey("new-consumer");
        resolved.Consumers["new-consumer"].Nodes.Should().HaveCount(1);
        resolved.Consumers["new-consumer"].Nodes[0].Id.Should().Be("new-node");
    }

    [Fact]
    public void Resolve_Overrides_ModifiesNodeConfig() {
        var baseProfile = MakeProfile("base", consumers: MakeConsumers(
            MakeConsumer("shell-pipeline",
                MakeNode("shell-timeout-keyword-mw", 100, config: MakeConfig("timeout", "60")))
        ));
        var childProfile = MakeProfile("child", inherits: "base", overrides: [
            new NodeProfileOverride("shell-pipeline", "shell-timeout-keyword-mw", null, MakeConfig("timeout", "600"))
        ]);
        var resolver = new NodeProfileResolver(MakeProfilesDict(baseProfile, childProfile));

        var resolved = resolver.Resolve("child");

        var node = resolved.Consumers["shell-pipeline"].Nodes
            .Should().ContainSingle(n => n.Id == "shell-timeout-keyword-mw").Subject;
        node.Config["timeout"].Should().Be("600");
    }

    [Fact]
    public void Resolve_Overrides_ModifiesNodePriority() {
        var baseProfile = MakeProfile("base", consumers: MakeConsumers(
            MakeConsumer("pipeline-a", MakeNode("node-a", 100))
        ));
        var childProfile = MakeProfile("child", inherits: "base", overrides: [
            new NodeProfileOverride("pipeline-a", "node-a", 500, null)
        ]);
        var resolver = new NodeProfileResolver(MakeProfilesDict(baseProfile, childProfile));

        var resolved = resolver.Resolve("child");

        resolved.Consumers["pipeline-a"].Nodes[0].Priority.Should().Be(500);
    }

    [Fact]
    public void Resolve_MultiLevelInheritance_ChainsCorrectly() {
        var baseProfile = MakeProfile("base", consumers: MakeConsumers(
            MakeConsumer("pipeline-a",
                MakeNode("node-a", 100), MakeNode("node-b", 200), MakeNode("node-c", 300))
        ));
        var middleProfile = MakeProfile("middle", inherits: "base", excludes: ["node-b"]);
        var leafProfile = MakeProfile("leaf", inherits: "middle", excludes: ["node-c"]);
        var resolver = new NodeProfileResolver(MakeProfilesDict(baseProfile, middleProfile, leafProfile));

        var resolved = resolver.Resolve("leaf");

        resolved.Name.Should().Be("leaf");
        resolved.Consumers["pipeline-a"].Nodes.Should().HaveCount(1);
        resolved.Consumers["pipeline-a"].Nodes[0].Id.Should().Be("node-a");
    }

    [Fact]
    public void Resolve_NonExistentProfile_Throws() {
        var resolver = new NodeProfileResolver(new Dictionary<string, NodeProfile>().ToFrozenDictionary());

        var act = () => resolver.Resolve("non-existent");
        act.Should().Throw<ArgumentException>();
    }

    // === Helpers ===

    private static NodeProfile MakeProfile(
        string name,
        string? inherits = null,
        Dictionary<string, ConsumerConfig>? consumers = null,
        string[]? excludes = null,
        NodeProfileOverride[]? overrides = null,
        Dictionary<string, ConsumerConfig>? includes = null
    ) => new(
        Name: name,
        Inherits: inherits,
        Consumers: (consumers ?? new()).ToFrozenDictionary(),
        Excludes: excludes is null ? FrozenSet<string>.Empty : FrozenSet.Create(excludes),
        Overrides: overrides ?? [],
        Includes: (includes ?? new()).ToFrozenDictionary()
    );

    private static NodeEntryConfig MakeNode(string id, int priority, string? flag = null, FrozenDictionary<string, string>? config = null)
        => new(id, priority, flag, config ?? FrozenDictionary<string, string>.Empty);

    private static ConsumerConfig MakeConsumer(string id, params NodeEntryConfig[] nodes)
        => new(id, nodes);

    private static Dictionary<string, ConsumerConfig> MakeConsumers(params ConsumerConfig[] consumers)
        => consumers.ToDictionary(c => c.ConsumerId, c => c);

    private static FrozenDictionary<string, string> MakeConfig(string key, string value)
        => new Dictionary<string, string> { [key] = value }.ToFrozenDictionary();

    private static FrozenDictionary<string, NodeProfile> MakeProfilesDict(params NodeProfile[] profiles)
        => profiles.ToDictionary(p => p.Name).ToFrozenDictionary();
}
