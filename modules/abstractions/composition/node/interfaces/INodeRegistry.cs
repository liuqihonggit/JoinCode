namespace JoinCode.Abstractions.Composition;

/// <summary>
/// 节点注册表接口 — 运行期统一管理所有已注册节点
/// 从 DI 容器收集所有 [Register] 注册的服务，统一包装为 INode
/// 消费点通过 Profile 配置从此注册表获取所需节点
/// </summary>
public interface INodeRegistry {
    /// <summary>按节点 ID 尝试获取单个节点</summary>
    bool TryGetNode(string nodeId, [NotNullWhen(true)] out INode? node);

    /// <summary>获取指定消费点的所有节点（按 Profile 配置和 Priority 排序，检查 enabled-when feature flag）</summary>
    Task<IReadOnlyList<INode>> GetNodesForConsumerAsync(string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default);

    /// <summary>获取指定消费点的所有服务实例（类型安全版本，检查 enabled-when feature flag）</summary>
    Task<IReadOnlyList<T>> GetServicesForConsumerAsync<T>(string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default) where T : class;
}
