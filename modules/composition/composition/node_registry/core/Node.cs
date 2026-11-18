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

    /// <summary>
    /// 构造运行期节点；metadata 由配置注入（G4），不再在构造函数里写死 Version/Capabilities。
    /// 未提供时落到 <see cref="NodeMetadata.Unspecified"/> 这个唯一默认值。
    /// </summary>
    internal Node(string nodeId, TService service, NodeMetadata? metadata = null) {
        NodeId = nodeId;
        Service = service;
        State = NodeState.Active;
        Metadata = metadata ?? NodeMetadata.Unspecified;
    }
}
