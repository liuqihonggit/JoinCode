namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// Profile 继承链解析器 — "省略即继承"合并器
/// 参考 Harness² Tree dict 模式：子 Profile 只声明差异，未提及的从父 Profile 继承
/// 合并顺序：排除 → 新增 → 覆写
/// </summary>
public sealed class NodeProfileResolver {
    private const int MaxInheritanceDepth = 3;
    private readonly FrozenDictionary<string, NodeProfile> _profiles;

    /// <summary>从已加载的 Profile 字典构建解析器</summary>
    public NodeProfileResolver(FrozenDictionary<string, NodeProfile> profiles) {
        _profiles = profiles;
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

    private static ResolvedProfile Merge(ResolvedProfile parent, NodeProfile child) {
        var consumers = new Dictionary<string, ResolvedConsumerConfig>(parent.Consumers, StringComparer.Ordinal);

        // 1. 排除子 Profile 明确移除的节点（从所有消费点中移除）
        foreach (var nodeId in child.Excludes) {
            foreach (var (consumerId, consumerConfig) in consumers) {
                var filtered = consumerConfig.Nodes.Where(n => n.Id != nodeId).ToArray();
                consumers[consumerId] = consumerConfig with { Nodes = filtered };
            }
        }

        // 2. 添加子 Profile 明确新增的消费点或节点
        foreach (var (consumerId, config) in child.Includes) {
            if (consumers.TryGetValue(consumerId, out var existing)) {
                var existingIds = existing.Nodes.Select(n => n.Id).ToFrozenSet(StringComparer.Ordinal);
                var newNodes = config.Nodes
                    .Where(n => !existingIds.Contains(n.Id))
                    .Select(n => new ResolvedNodeEntry(n.Id, n.Priority, n.EnabledWhenFeatureFlag, n.Config))
                    .ToArray();
                var combined = existing.Nodes.Concat(newNodes).ToArray();
                consumers[consumerId] = existing with { Nodes = combined };
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
                    return n with {
                        Priority = overrideEntry.Priority ?? n.Priority,
                        Config = overrideEntry.Config ?? n.Config
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
