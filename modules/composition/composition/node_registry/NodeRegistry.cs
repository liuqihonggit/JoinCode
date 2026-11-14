namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 节点注册表 — 运行期统一管理所有已注册节点
/// 从 DI 容器收集所有 [Register] 注册的服务，统一包装为 INode
/// 消费点通过 Profile 配置从此注册表获取所需节点
/// </summary>
public sealed class NodeRegistry : INodeRegistry {
    private readonly FrozenDictionary<string, NodeEntry> _nodes;
    private readonly IServiceProvider _services;
    private readonly IFeatureFlagService? _featureFlagService;

    private NodeRegistry(IServiceProvider services, FrozenDictionary<string, NodeEntry> nodes, IFeatureFlagService? featureFlagService) {
        _services = services;
        _nodes = nodes;
        _featureFlagService = featureFlagService;
    }

    /// <summary>从 DI 容器构建节点注册表</summary>
    public static NodeRegistry Build(IServiceProvider sp, IEnumerable<KeyValuePair<string, Func<IServiceProvider, INode>>>? customFactories = null) {
        var nodes = new Dictionary<string, NodeEntry>(StringComparer.Ordinal);

        if (customFactories is not null) {
            foreach (var (nodeId, factory) in customFactories) {
                nodes[nodeId] = new NodeEntry(nodeId, factory);
            }
        }

        var featureFlagService = sp.GetService<IFeatureFlagService>();
        return new NodeRegistry(sp, nodes.ToFrozenDictionary(StringComparer.Ordinal), featureFlagService);
    }

    /// <summary>注册单个节点</summary>
    public void Register(string nodeId, Func<IServiceProvider, INode> factory) {
        if (!_nodes.TryGetValue(nodeId, out _)) {
            throw new InvalidOperationException($"NodeRegistry is immutable; cannot add '{nodeId}' after build");
        }
    }

    /// <summary>按节点 ID 尝试获取单个节点</summary>
    public bool TryGetNode(string nodeId, [NotNullWhen(true)] out INode? node) {
        if (_nodes.TryGetValue(nodeId, out var entry)) {
            node = entry.CreateNode(_services);
            return true;
        }
        node = null;
        return false;
    }

    /// <summary>获取指定消费点的所有节点（按 Profile 配置和 Priority 排序，检查 enabled-when feature flag）</summary>
    public async Task<IReadOnlyList<INode>> GetNodesForConsumerAsync(string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default) {
        if (!profile.Consumers.TryGetValue(consumerId, out var consumerConfig)) return [];
        var result = new List<INode>();
        foreach (var nodeEntry in consumerConfig.Nodes.OrderBy(n => n.Priority)) {
            if (nodeEntry.EnabledWhenFeatureFlag is not null && _featureFlagService is not null) {
                var isEnabled = await _featureFlagService.IsEnabledAsync(
                    nodeEntry.EnabledWhenFeatureFlag, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!isEnabled) continue;
            }
            if (_nodes.TryGetValue(nodeEntry.Id, out var entry))
                result.Add(entry.CreateNode(_services));
        }
        return result;
    }

    /// <summary>获取指定消费点的所有服务实例（类型安全版本，检查 enabled-when feature flag）</summary>
    public async Task<IReadOnlyList<T>> GetServicesForConsumerAsync<T>(string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default) where T : notnull {
        var nodes = await GetNodesForConsumerAsync(consumerId, profile, cancellationToken).ConfigureAwait(false);
        var result = new List<T>();
        foreach (var node in nodes) {
            if (node is Node<T> typedNode)
                result.Add(typedNode.Service);
        }
        return result;
    }
}
