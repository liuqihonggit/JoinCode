namespace JoinCode.Abstractions.Composition.Profile;

/// <summary>
/// 消费点配置 — 一个消费点包含一组有序的节点
/// </summary>
public sealed record ConsumerConfig(
    string ConsumerId,
    IReadOnlyList<NodeEntryConfig> Nodes
);
