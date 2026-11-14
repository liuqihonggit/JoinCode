namespace JoinCode.Composition.NodeRegistry.Consumers;

/// <summary>
/// 命令分发消费点 — 将命令分发纳入 NodeRegistry 统一管理
/// </summary>
public sealed class CommandDispatchConsumer : INodeConsumer {
    /// <summary>消费点唯一标识</summary>
    public string ConsumerId { get; }

    /// <summary>构造命令分发消费点</summary>
    /// <param name="consumerId">消费点 ID（如 "command-dispatch"）</param>
    public CommandDispatchConsumer(string consumerId = "command-dispatch") {
        ArgumentException.ThrowIfNullOrEmpty(consumerId);
        ConsumerId = consumerId;
    }

    /// <summary>从注册表中解析此消费点所需的节点列表</summary>
    public Task<IReadOnlyList<INode>> ResolveNodesAsync(INodeRegistry registry, ResolvedProfile profile, CancellationToken cancellationToken = default) =>
        registry.GetNodesForConsumerAsync(ConsumerId, profile, cancellationToken);
}
