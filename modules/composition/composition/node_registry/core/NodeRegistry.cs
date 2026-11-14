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
    private readonly ILogger<NodeRegistry>? _logger;

    private NodeRegistry(IServiceProvider services, FrozenDictionary<string, NodeEntry> nodes, IFeatureFlagService? featureFlagService, ILogger<NodeRegistry>? logger) {
        _services = services;
        _nodes = nodes;
        _featureFlagService = featureFlagService;
        _logger = logger;
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
        var logger = sp.GetService<ILoggerFactory>()?.CreateLogger<NodeRegistry>();
        return new NodeRegistry(sp, nodes.ToFrozenDictionary(StringComparer.Ordinal), featureFlagService, logger);
    }

    /// <summary>注册单个节点 — 不可变注册表不支持运行时注册</summary>
    public void Register(string nodeId, Func<IServiceProvider, INode> factory) {
        throw new NotSupportedException($"NodeRegistry is immutable; cannot register '{nodeId}' after build. Use NodeRegistry.Build() to create a new registry with additional factories.");
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
            if (nodeEntry.EnabledWhenFeatureFlag is not null) {
                if (_featureFlagService is null) continue;
                var isEnabled = await _featureFlagService.IsEnabledAsync(
                    nodeEntry.EnabledWhenFeatureFlag, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!isEnabled) continue;
            }
            if (_nodes.TryGetValue(nodeEntry.Id, out var entry))
                result.Add(entry.CreateNode(_services));
            else
                _logger?.LogWarning("Node '{NodeId}' required by consumer '{ConsumerId}' is not registered in NodeRegistry", nodeEntry.Id, consumerId);
        }
        return result;
    }

    /// <summary>获取指定消费点的所有服务实例（类型安全版本，检查 enabled-when feature flag）</summary>
    public async Task<IReadOnlyList<T>> GetServicesForConsumerAsync<T>(string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default) where T : class {
        if (!profile.Consumers.TryGetValue(consumerId, out var consumerConfig)) return [];
        var resolver = _services.GetService<NodeServiceResolver>();
        var result = new List<T>();
        foreach (var nodeEntry in consumerConfig.Nodes.OrderBy(n => n.Priority)) {
            if (nodeEntry.EnabledWhenFeatureFlag is not null) {
                if (_featureFlagService is null) continue;
                var isEnabled = await _featureFlagService.IsEnabledAsync(
                    nodeEntry.EnabledWhenFeatureFlag, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!isEnabled) continue;
            }
            if (resolver is not null && resolver.TryResolve<T>(consumerId, nodeEntry.Id, _services, out var service)) {
                result.Add(service);
            } else if (_nodes.TryGetValue(nodeEntry.Id, out var entry) && entry.CreateNode(_services) is Node<T> typedNode) {
                result.Add(typedNode.Service);
            } else {
                _logger?.LogWarning("Node '{NodeId}' in consumer '{ConsumerId}' could not be resolved for service type '{ServiceType}'", nodeEntry.Id, consumerId, typeof(T).Name);
            }
        }
        return result;
    }
}
