namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 节点注册条目 — 内部数据结构，存储节点的工厂函数
/// </summary>
internal sealed class NodeEntry {
    private readonly Func<IServiceProvider, INode> _factory;

    internal NodeEntry(string nodeId, Func<IServiceProvider, INode> factory) {
        NodeId = nodeId;
        _factory = factory;
    }

    /// <summary>节点唯一标识</summary>
    public string NodeId { get; }

    /// <summary>从 DI 容器创建节点实例</summary>
    public INode CreateNode(IServiceProvider sp) => _factory(sp);

    /// <summary>创建指定服务类型的节点注册条目</summary>
    public static NodeEntry Create<TService>(string nodeId) where TService : notnull {
        return new NodeEntry(nodeId, sp => {
            var service = sp.GetRequiredService<TService>();
            return new Node<TService>(nodeId, service);
        });
    }
}
