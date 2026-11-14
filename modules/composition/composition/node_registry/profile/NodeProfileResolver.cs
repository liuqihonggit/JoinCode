namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// Profile 继承链解析器 — "省略即继承"合并器
/// 参考 Harness² Tree dict 模式：子 Profile 只声明差异，未提及的从父 Profile 继承
/// 合并顺序：排除 → 新增 → 覆写
/// </summary>
public sealed class NodeProfileResolver {
    private const int MaxInheritanceDepth = 3;
    private readonly FrozenDictionary<string, NodeProfile> _profiles;
    private readonly ILogger<NodeProfileResolver>? _logger;

    /// <summary>从已加载的 Profile 字典构建解析器</summary>
    public NodeProfileResolver(FrozenDictionary<string, NodeProfile> profiles, ILogger<NodeProfileResolver>? logger = null) {
        _profiles = profiles;
        _logger = logger;
    }

    /// <summary>解析指定 Profile 的完整继承链，返回合并后的 ResolvedProfile</summary>
    public ResolvedProfile Resolve(string profileName) {
        return Resolve(profileName, [], 0);
    }

    private ResolvedProfile Resolve(string profileName, HashSet<string> visited, int depth) {
        if (!_profiles.TryGetValue(profileName, out var profile))
            throw new ArgumentException($"Profile '{profileName}' not found. Available: {string.Join(", ", _profiles.Keys.Order())}");

        if (depth > MaxInheritanceDepth)
            throw new InvalidOperationException($"Profile '{profileName}' inheritance chain exceeds max depth {MaxInheritanceDepth}");

        if (profile.Inherits is null) return ResolveSelf(profile);

        if (visited.Contains(profileName))
            throw new InvalidOperationException($"Circular inheritance detected: '{profileName}' is already in the chain [{string.Join(" → ", visited.Append(profileName))}]");

        visited.Add(profileName);
        var parent = Resolve(profile.Inherits, visited, depth + 1);
        visited.Remove(profileName);
        return Merge(parent, profile);
    }

    private static ResolvedProfile ResolveSelf(NodeProfile profile) {
        var consumers = new Dictionary<string, ResolvedConsumerConfig>(StringComparer.Ordinal);
        foreach (var (consumerId, config) in profile.Consumers) {
            var nodes = config.Nodes
                .Select(n => new ResolvedNodeEntry(n.Id, n.Priority, n.EnabledWhenFeatureFlag, n.Config))
                .ToArray();
            consumers[consumerId] = new ResolvedConsumerConfig(consumerId, nodes);
        }
        return new ResolvedProfile(profile.Name, consumers.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static FrozenDictionary<string, string> MergeConfig(IReadOnlyDictionary<string, string> baseConfig, IReadOnlyDictionary<string, string> overrideConfig) {
        var merged = new Dictionary<string, string>(baseConfig, StringComparer.Ordinal);
        foreach (var (key, value) in overrideConfig)
            merged[key] = value;
        return merged.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private ResolvedProfile Merge(ResolvedProfile parent, NodeProfile child) {
        var consumers = new Dictionary<string, ResolvedConsumerConfig>(parent.Consumers, StringComparer.Ordinal);

        // 1. 排除子 Profile 明确移除的节点（从所有消费点中移除）
        foreach (var nodeId in child.Excludes) {
            var foundAny = false;
            foreach (var (consumerId, consumerConfig) in consumers) {
                if (consumerConfig.Nodes.Any(n => n.Id == nodeId)) {
                    foundAny = true;
                    var filtered = consumerConfig.Nodes.Where(n => n.Id != nodeId).ToArray();
                    consumers[consumerId] = consumerConfig with { Nodes = filtered };
                }
            }
            if (!foundAny)
                _logger?.LogWarning("Profile '{ProfileName}' excludes node '{NodeId}' but it was not found in any consumer", child.Name, nodeId);
        }

        // 2. 添加子 Profile 明确新增的消费点或节点
        foreach (var (consumerId, config) in child.Includes) {
            if (consumers.TryGetValue(consumerId, out var existing)) {
                var existingNodes = existing.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
                foreach (var newNode in config.Nodes) {
                    var resolved = new ResolvedNodeEntry(newNode.Id, newNode.Priority, newNode.EnabledWhenFeatureFlag, newNode.Config ?? FrozenDictionary<string, string>.Empty);
                    if (existingNodes.TryGetValue(newNode.Id, out var existingNode)) {
                        var mergedConfig = newNode.Config is not null && newNode.Config.Count > 0
                            ? MergeConfig(existingNode.Config, newNode.Config)
                            : existingNode.Config;
                        existingNodes[newNode.Id] = existingNode with {
                            Priority = newNode.Priority != 0 ? newNode.Priority : existingNode.Priority,
                            Config = mergedConfig
                        };
                    } else {
                        existingNodes[newNode.Id] = resolved;
                    }
                }
                consumers[consumerId] = existing with { Nodes = existingNodes.Values.OrderBy(n => n.Priority).ToArray() };
            } else {
                var nodes = config.Nodes
                    .Select(n => new ResolvedNodeEntry(n.Id, n.Priority, n.EnabledWhenFeatureFlag, n.Config))
                    .ToArray();
                consumers[consumerId] = new ResolvedConsumerConfig(consumerId, nodes);
            }
        }

        // 3. 覆写子 Profile 明确修改的节点属性
        foreach (var overrideEntry in child.Overrides) {
            if (consumers.TryGetValue(overrideEntry.Consumer, out var consumerConfig)) {
                var found = false;
                var nodes = consumerConfig.Nodes.Select(n => {
                    if (n.Id != overrideEntry.Node) return n;
                    found = true;
                    var mergedConfig = overrideEntry.Config is not null
                        ? MergeConfig(n.Config, overrideEntry.Config)
                        : n.Config;
                    return n with {
                        Priority = overrideEntry.Priority ?? n.Priority,
                        Config = mergedConfig
                    };
                }).ToArray();
                if (!found)
                    throw new InvalidOperationException($"Override references non-existent node '{overrideEntry.Node}' in consumer '{overrideEntry.Consumer}' of profile '{child.Name}'");
                consumers[overrideEntry.Consumer] = consumerConfig with { Nodes = nodes };
            } else {
                throw new InvalidOperationException($"Override references non-existent consumer '{overrideEntry.Consumer}' in profile '{child.Name}'");
            }
        }

        return new ResolvedProfile(child.Name, consumers.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
