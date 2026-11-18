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
/// <param name="ConsumerId">消费点 ID。</param>
/// <param name="Nodes">合并后的节点条目（按 priority 升序）。</param>
/// <param name="Contract">装配契约，已解析为具体值（缺省 middleware）。</param>
public sealed record ResolvedConsumerConfig(
    string ConsumerId,
    IReadOnlyList<ResolvedNodeEntry> Nodes,
    string Contract = NodeContract.Middleware
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
