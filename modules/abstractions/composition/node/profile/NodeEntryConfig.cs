namespace JoinCode.Abstractions.Composition.Profile;

/// <summary>
/// 消费点中单个节点的配置条目
/// </summary>
public sealed record NodeEntryConfig(
    string Id,
    int Priority,
    string? EnabledWhenFeatureFlag,
    FrozenDictionary<string, string> Config
);
