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

    /// <summary>
    /// 运行期注册节点并返回可回滚句柄（G1，DSH ctx.effect 语义）。
    /// 消费方按接口注入即可装卸节点，不必依赖具体 NodeRegistry 类型。
    /// </summary>
    RegistrationHandle Register(string nodeId, Func<IServiceProvider, NodeMetadata?, INode> factory);

    /// <summary>获取指定消费点的所有服务实例（类型安全版本，检查 enabled-when feature flag）</summary>
    Task<IReadOnlyList<T>> GetServicesForConsumerAsync<T>(string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default) where T : class;
}
