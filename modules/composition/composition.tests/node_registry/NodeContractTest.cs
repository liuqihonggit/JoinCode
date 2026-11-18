namespace JoinCode.Composition.NodeRegistry.Tests;

/// <summary>
/// 消费点契约（TASK100）— 解析、缺省、继承与非法值拦截。
/// <para>契约决定「节点如何组合」，是节点形态不再被 DI 类型绑架的前提；
/// 缺省必须是 middleware，否则 165 条既有声明会全部失效。</para>
/// </summary>
public sealed class NodeContractTest {
    [Fact]
    public void ParseYaml_NoContract_LeavesNull() {
        const string yaml = """
name: base
consumers:
  chat-pipeline:
    nodes:
      - id: stream-rate-limit-mw
        priority: 100
""";

        var profile = NodeProfileLoader.ParseYaml("base", yaml);

        profile.Consumers["chat-pipeline"].Contract.Should().BeNull("YAML 未声明时交由继承链决定，不能在解析层臆造值");
    }

    [Fact]
    public void ParseYaml_ContractDeclared_NormalizedToLowerCase() {
        const string yaml = """
name: base
consumers:
  guard-chain:
    contract: GUARD
    nodes:
      - id: git-commit-guard
""";

        var profile = NodeProfileLoader.ParseYaml("base", yaml);

        profile.Consumers["guard-chain"].Contract.Should().Be(NodeContract.Guard);
    }

    [Fact]
    public void ParseYaml_InvalidContract_ThrowsListingLegalValues() {
        const string yaml = """
name: base
consumers:
  guard-chain:
    contract: middlewares
    nodes:
      - id: git-commit-guard
""";

        var act = () => NodeProfileLoader.ParseYaml("base", yaml);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*middlewares*非法*")
            .WithMessage($"*{NodeContract.Guard}*");
    }

    [Fact]
    public void Resolve_ContractOmitted_DefaultsToMiddleware() {
        var profile = MakeProfile("base", MakeConsumer("chat-pipeline", null));
        var resolver = new NodeProfileResolver(MakeProfilesDict(profile));

        var resolved = resolver.Resolve("base");

        resolved.Consumers["chat-pipeline"].Contract.Should().Be(NodeContract.Middleware);
    }

    [Fact]
    public void Resolve_ChildOmitsContract_InheritsParentContract() {
        var baseProfile = MakeProfile("base", MakeConsumer("sandbox-providers", NodeContract.Provider));
        var child = MakeProfile("sdk", inherits: "base");
        var resolver = new NodeProfileResolver(MakeProfilesDict(baseProfile, child));

        var resolved = resolver.Resolve("sdk");

        resolved.Consumers["sandbox-providers"].Contract.Should().Be(NodeContract.Provider, "子 profile 未声明即继承父级契约");
    }

    [Fact]
    public void Resolve_ChildDeclaresContract_OverridesParent() {
        var baseProfile = MakeProfile("base", MakeConsumer("agent-extras", NodeContract.Middleware));
        var child = MakeProfile("sdk", inherits: "base", includes: new Dictionary<string, ConsumerConfig>(StringComparer.Ordinal) {
            ["agent-extras"] = new ConsumerConfig("agent-extras", [new NodeEntryConfig("executor-readonly", 100, null, FrozenDictionary<string, string>.Empty)], NodeContract.Role),
        });
        var resolver = new NodeProfileResolver(MakeProfilesDict(baseProfile, child));

        var resolved = resolver.Resolve("sdk");

        resolved.Consumers["agent-extras"].Contract.Should().Be(NodeContract.Role);
    }

    private static NodeProfile MakeProfile(
        string name,
        ConsumerConfig? consumer = null,
        string? inherits = null,
        Dictionary<string, ConsumerConfig>? includes = null) {
        var consumers = consumer is null
            ? new Dictionary<string, ConsumerConfig>(StringComparer.Ordinal)
            : new Dictionary<string, ConsumerConfig>(StringComparer.Ordinal) { [consumer.ConsumerId] = consumer };
        return new NodeProfile(
            Name: name,
            Inherits: inherits,
            Consumers: consumers.ToFrozenDictionary(StringComparer.Ordinal),
            Excludes: FrozenSet<string>.Empty,
            Overrides: [],
            Includes: (includes ?? new Dictionary<string, ConsumerConfig>(StringComparer.Ordinal)).ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static ConsumerConfig MakeConsumer(string id, string? contract) =>
        new(id, [new NodeEntryConfig("node-a", 100, null, FrozenDictionary<string, string>.Empty)], contract);

    private static FrozenDictionary<string, NodeProfile> MakeProfilesDict(params NodeProfile[] profiles) =>
        profiles.ToDictionary(p => p.Name, StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);
}
