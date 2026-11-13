namespace JoinCode.Abstractions.Composition;

/// <summary>
/// 节点统一接口 — "万物皆节点"的运行期抽象
/// 节点本身无 NodeKind 固有属性，由消费点通过 Profile 配置决定如何组合
/// </summary>
public interface INode {
    /// <summary>节点唯一标识（如 "shell"、"crash-snapshot-mw"）</summary>
    string NodeId { get; }

    /// <summary>节点元数据</summary>
    NodeMetadata Metadata { get; }

    /// <summary>当前生命周期状态</summary>
    NodeState State { get; }
}
