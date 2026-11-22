namespace JoinCode.Composition.NodeRegistry.Tests;

/// <summary>
/// TASK097 §15（H2/H3）回归 — G1 插件节点桥接的可逆注册 + metadata 能力路由。
/// <para>
/// H2：PluginNodeBridge 把插件加载/卸载接到 INodeRegistry.Register/RegistrationHandle.Dispose，
/// 让「装什么撤什么」由注册表兑现（DSH ctx.effect）；同一插件重复加载按 LIFO 回滚。
/// H3：meta.required-capabilities 参与路由，任一必需能力未开则节点被跳过；
/// 与 feature flag 同类语义（设计内跳过，不算 S10 的装配失败），两个解析入口同口径。
/// 全部用真实 NodeRegistry（非 mock）。
/// </para>
/// </summary>
public sealed class NodeBridgeAndCapabilityTest {
    private const string ConsumerId = "bridge-consumer";
    private const string NodeId = "bridged-service";

    private sealed class ProbeService { }

    private sealed class EmptyServiceProvider : IServiceProvider {
        public object? GetService(Type serviceType) => null;
    }

    private static NodeRegistry BuildRegistry() =>
        NodeRegistry.Build(new EmptyServiceProvider(), [
            new KeyValuePair<string, Func<IServiceProvider, NodeMetadata?, INode>>(
                $"{ConsumerId}:{NodeId}",
                (_, meta) => new Node<ProbeService>(NodeId, new ProbeService(), meta))
        ]);

    private static ResolvedProfile ProfileWithMeta(params (string Id, string RequiredCaps)[] nodes) =>
        new("test", new Dictionary<string, ResolvedConsumerConfig> {
            [ConsumerId] = new(ConsumerId, nodes
                .Select(n => {
                    var config = string.IsNullOrEmpty(n.RequiredCaps)
                        ? FrozenDictionary<string, string>.Empty
                        : new Dictionary<string, string> { [NodeMetadata.ConfigPrefix + "required-capabilities"] = n.RequiredCaps }
                            .ToFrozenDictionary(StringComparer.Ordinal);
                    return new ResolvedNodeEntry(n.Id, 100, null, config);
                })
                .ToList())
        }.ToFrozenDictionary(StringComparer.Ordinal));

    // ===== H2：插件桥可逆注册 =====

    [Fact]
    public void PluginBridge_LoadRegistersNode_UnloadRollsBack() {
        var registry = BuildRegistry();
        var bridge = new PluginNodeBridge(registry);

        bridge.OnPluginLoaded("demo-plugin");
        Assert.True(registry.TryGetNode("plugin:demo-plugin", out var node));
        Assert.NotNull(node);
        Assert.Equal("plugin:demo-plugin", node.NodeId);
        Assert.Equal("plugin", node.Metadata.Version);   // 来自运行期注册的元数据
        Assert.Equal(1, bridge.TrackedCount);

        bridge.OnPluginUnloading("demo-plugin");
        Assert.False(registry.TryGetNode("plugin:demo-plugin", out _));
        Assert.Equal(0, bridge.TrackedCount);
    }

    [Fact]
    public void PluginBridge_RepeatLoad_UnloadsInReverseOrder() {
        var registry = BuildRegistry();
        var bridge = new PluginNodeBridge(registry);

        bridge.OnPluginLoaded("reload");
        bridge.OnPluginLoaded("reload");       // 同名重复加载（模拟重载）
        Assert.Equal(2, bridge.TrackedCount);
        bridge.OnPluginUnloading("reload");    // 一次卸载应撤掉全部句柄
        Assert.False(registry.TryGetNode("plugin:reload", out _));
        Assert.Equal(0, bridge.TrackedCount);
    }

    // ===== H3：能力路由 =====

    [Fact]
    public async Task RequiredCapabilities_Omitted_KeepsNode() {
        // 零回归：生产节点均无 required-capabilities → 不过滤。
        var services = await BuildRegistry().GetServicesForConsumerAsync<ProbeService>(
            ConsumerId, ProfileWithMeta((NodeId, "")));
        Assert.Single(services);
    }

    [Fact]
    public async Task RequiredCapabilities_ProfileWithCapability_KeepsNode() {
        // capabilities 消费点未声明 = 全开 → 必需能力满足 → 节点保留。
        var profile = new ResolvedProfile("test", new Dictionary<string, ResolvedConsumerConfig> {
            [ConsumerId] = new(ConsumerId, [new ResolvedNodeEntry(NodeId, 100, null,
                new Dictionary<string, string> { [NodeMetadata.ConfigPrefix + "required-capabilities"] = "todo" }
                    .ToFrozenDictionary(StringComparer.Ordinal))])
        }.ToFrozenDictionary(StringComparer.Ordinal));
        var services = await BuildRegistry().GetServicesForConsumerAsync<ProbeService>(ConsumerId, profile);
        Assert.Single(services);
    }

    [Fact]
    public async Task RequiredCapabilities_DisabledInProfile_SkipsNodeInBothEntries() {
        // profile 显式关掉 capabilities 里的 todo → 必需能力不满足 → 两个解析入口都跳过。
        var profile = new ResolvedProfile("test", new Dictionary<string, ResolvedConsumerConfig> {
            [ConsumerId] = new(ConsumerId, [new ResolvedNodeEntry(NodeId, 100, null,
                new Dictionary<string, string> { [NodeMetadata.ConfigPrefix + "required-capabilities"] = "todo" }
                    .ToFrozenDictionary(StringComparer.Ordinal))]),
            [CapabilityNodes.ConsumerId] = new(CapabilityNodes.ConsumerId, [])  // 白名单空 = 全关
        }.ToFrozenDictionary(StringComparer.Ordinal));
        var registry = BuildRegistry();

        var services = await registry.GetServicesForConsumerAsync<ProbeService>(ConsumerId, profile);
        Assert.Empty(services);

        var nodes = await registry.GetNodesForConsumerAsync(ConsumerId, profile);
        Assert.Empty(nodes);
    }
}
