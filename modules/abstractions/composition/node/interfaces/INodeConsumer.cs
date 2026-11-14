namespace JoinCode.Abstractions.Composition;

/// <summary>
/// 消费点接口 — 消费点从 NodeRegistry 中按 Profile 配置解析所需的节点列表
/// 消费点驱动：NodeKind 不是节点的固有属性，而是消费点的分类需求
/// 一个节点可出现在多个消费点中，由 YAML 配置自由搭配
/// </summary>
public interface INodeConsumer {
    /// <summary>消费点唯一标识（如 "chat-pipeline"、"tool-dispatch"）</summary>
    string ConsumerId { get; }

    /// <summary>从注册表中解析此消费点所需的节点列表</summary>
    Task<IReadOnlyList<INode>> ResolveNodesAsync(INodeRegistry registry, ResolvedProfile profile, CancellationToken cancellationToken = default);
}
