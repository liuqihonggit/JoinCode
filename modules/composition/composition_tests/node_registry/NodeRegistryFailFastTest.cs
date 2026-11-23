namespace JoinCode.Composition.NodeRegistry.Tests;

/// <summary>
/// TASK100 S10 / §25.7 D6 回归 — <c>GetServicesForConsumerAsync</c> 的 <c>failFastOnMissing</c>。
/// <para>
/// 断裂点 F6：过去节点解析不到时只记一条 Warning 就丢掉。在 command-dispatch /
/// prompt-assembly 这类**实例型**消费点上，丢一个节点等于「整类斜杠命令消失」或
/// 「提示词整组段落不见」，现象（功能没了）与原因（工厂没解析出来）相隔很远。
/// 本测试用真实 <see cref="NodeRegistry"/>（非 mock）锁住四件事：
/// 默认行为保持兼容、开关打开会抛错且指名道姓、全解析成功时不误伤、
/// feature flag 关闭仍按设计跳过（不算解析失败）。
/// </para>
/// </summary>
public sealed class NodeRegistryFailFastTest {
    private const string ConsumerId = "instance-consumer";
    private const string PresentNode = "present-node";
    private const string GhostNode = "ghost-node";

    private sealed class ProbeService { }

    private sealed class EmptyServiceProvider : IServiceProvider {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>只注册一个可解析节点 —— 幽灵节点故意不登记，模拟「profile 声明了但工厂漏了」。</summary>
    private static NodeRegistry BuildRegistry() =>
        NodeRegistry.Build(new EmptyServiceProvider(), [
            new KeyValuePair<string, Func<IServiceProvider, NodeMetadata?, INode>>(
                $"{ConsumerId}:{PresentNode}",
                (_, meta) => new Node<ProbeService>(PresentNode, new ProbeService(), meta))
        ]);

    private static ResolvedProfile Profile(params (string Id, string? FeatureFlag)[] nodes) =>
        new("test", new Dictionary<string, ResolvedConsumerConfig> {
            [ConsumerId] = new(ConsumerId, nodes
                .Select(n => new ResolvedNodeEntry(n.Id, 100, n.FeatureFlag, FrozenDictionary<string, string>.Empty))
                .ToList())
        }.ToFrozenDictionary(StringComparer.Ordinal));

    [Fact]
    public async Task DefaultFalse_SkipsUnresolvedNode_KeepingHistoricalBehavior() {
        // 默认 false 是兼容口径：调用方没显式要求就得保持过去的「记 Warning 跳过」。
        // 这条测试本身描述的正是 F6 —— 所以新增实例型消费点必须显式传 true（门禁锁）。
        var services = await BuildRegistry().GetServicesForConsumerAsync<ProbeService>(
            ConsumerId, Profile((PresentNode, null), (GhostNode, null)));

        Assert.Single(services);
    }

    [Fact]
    public async Task FailFastTrue_Throws_AndNamesConsumerNodeAndExpectedType() {
        Func<Task> act = async () => await BuildRegistry().GetServicesForConsumerAsync<ProbeService>(
            ConsumerId, Profile((PresentNode, null), (GhostNode, null)),
            default, failFastOnMissing: true);

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message.Should()
            .Contain(ConsumerId).And
            .Contain(GhostNode).And
            .Contain(nameof(ProbeService));
    }

    [Fact]
    public async Task FailFastTrue_DoesNotThrow_WhenEveryDeclaredNodeResolves() {
        // 正向：开开关后正常 profile 必须照常装配 —— 不能把「严格」变成「一启动就炸」。
        var services = await BuildRegistry().GetServicesForConsumerAsync<ProbeService>(
            ConsumerId, Profile((PresentNode, null)),
            default, failFastOnMissing: true);

        Assert.Single(services);
    }

    [Fact]
    public async Task FailFastTrue_DoesNotTreatDisabledFeatureFlagAsMissing() {
        // enabled-when 是 registry 内部的显式跳过分支，不是解析失败。
        // 容器里没有 IFeatureFlagService 时该分支直接 continue —— 加 failFast 后仍不该抛。
        var services = await BuildRegistry().GetServicesForConsumerAsync<ProbeService>(
            ConsumerId, Profile((GhostNode, "some.disabled.flag")),
            default, failFastOnMissing: true);

        Assert.Empty(services);
    }
}
