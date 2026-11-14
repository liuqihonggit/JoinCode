namespace JoinCode.Composition.NodeRegistry.Consumers;

/// <summary>
/// 管道消费点 — 将管道中间件组装纳入 NodeRegistry 统一管理
/// </summary>
public sealed class PipelineConsumer : INodeConsumer {
    /// <summary>消费点唯一标识（如 "chat-pipeline"、"tool-execution-pipeline"）</summary>
    public string ConsumerId { get; }

    /// <summary>构造管道消费点</summary>
    /// <param name="pipelineId">管道消费点 ID</param>
    public PipelineConsumer(string pipelineId) {
        ArgumentException.ThrowIfNullOrEmpty(pipelineId);
        ConsumerId = pipelineId;
    }

    /// <summary>从注册表中解析此消费点所需的节点列表</summary>
    public Task<IReadOnlyList<INode>> ResolveNodesAsync(INodeRegistry registry, ResolvedProfile profile, CancellationToken cancellationToken = default) =>
        registry.GetNodesForConsumerAsync(ConsumerId, profile, cancellationToken);
}
