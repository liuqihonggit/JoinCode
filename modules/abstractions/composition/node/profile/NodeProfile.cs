namespace JoinCode.Abstractions.Composition.Profile;

/// <summary>
/// NodeProfile — 数据驱动的节点组合配置（YAML 加载后的数据结构）
/// 支持"省略即继承"：子 Profile 只声明差异，未提及的从父 Profile 继承
/// </summary>
public sealed record NodeProfile(
    string Name,
    string? Inherits,
    FrozenDictionary<string, ConsumerConfig> Consumers,
    FrozenSet<string> Excludes,
    IReadOnlyList<NodeProfileOverride> Overrides,
    FrozenDictionary<string, ConsumerConfig> Includes
);
