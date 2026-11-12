namespace JoinCode.Abstractions.Composition.Profile;

/// <summary>
/// 合并后的最终 Profile — 继承链解析完毕的消费点→节点映射
/// </summary>
public sealed record ResolvedProfile(
    string Name,
    FrozenDictionary<string, ResolvedConsumerConfig> Consumers
);

/// <summary>
/// 合并后的消费点配置
/// </summary>
public sealed record ResolvedConsumerConfig(
    string ConsumerId,
    IReadOnlyList<ResolvedNodeEntry> Nodes
);

/// <summary>
/// 合并后的单个节点条目
/// </summary>
public sealed record ResolvedNodeEntry(
    string Id,
    int Priority,
    string? EnabledWhenFeatureFlag,
    FrozenDictionary<string, string> Config
);
