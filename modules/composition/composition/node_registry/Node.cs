namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 运行期节点适配器 — 将 DI 容器中的服务包装为 INode
/// </summary>
internal sealed class Node<TService> : INode {
    /// <summary>节点唯一标识</summary>
    public string NodeId { get; }

    /// <summary>节点元数据</summary>
    public NodeMetadata Metadata { get; }

    /// <summary>节点生命周期状态</summary>
    public NodeState State { get; internal set; }

    /// <summary>底层服务实例</summary>
    public TService Service { get; }

    internal Node(string nodeId, TService service) {
        NodeId = nodeId;
        Service = service;
        State = NodeState.Active;
        Metadata = new NodeMetadata(
            Version: "1.0",
            Dependencies: [],
            Capabilities: [],
            RequiredCapabilities: [],
            Priority: 0,
            EnabledWhenFeatureFlag: null
        );
    }
}
