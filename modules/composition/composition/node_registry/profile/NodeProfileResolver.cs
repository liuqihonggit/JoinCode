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

    /// <summary>可用 Profile 名称列表</summary>
    public IReadOnlyList<string> AvailableProfiles => _profiles.Keys.Order().ToArray();

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
            consumers[consumerId] = new ResolvedConsumerConfig(consumerId, nodes, config.Contract ?? NodeContract.Middleware);
        }
        return new ResolvedProfile(profile.Name, consumers.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// 解析一条 excludes 条目：<c>consumer:node</c> → (consumer, node)；<c>node</c> → (null, node)。
    /// <para>
    /// 判定「前缀是否真是消费点」而不是「是否含冒号」：节点 ID 的全局命名空间里没有禁止冒号的规则，
    /// 直接按冒号劈开会把一个合法节点 ID 拆成两半，且错误形态是**静默少一个节点**（不是报错），
    /// 排查成本极高。用父级已有的消费点做白名单，误判方向就变成无害的「整串当节点 ID」。
    /// </para>
    /// </summary>
    /// <param name="entry">excludes 中的一条原文。</param>
    /// <param name="consumers">合并中的消费点集合（父级已解析结果）。</param>
    /// <returns>消费点限定符（无则 null）与实际节点 ID。</returns>
    private static (string? Consumer, string Node) SplitExclude(string entry, IReadOnlyDictionary<string, ResolvedConsumerConfig> consumers) {
        var separator = entry.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0) return (null, entry);

        var prefix = entry[..separator];
        return consumers.ContainsKey(prefix) ? (prefix, entry[(separator + 1)..]) : (null, entry);
    }

    private static FrozenDictionary<string, string> MergeConfig(IReadOnlyDictionary<string, string> baseConfig, IReadOnlyDictionary<string, string> overrideConfig) {
        var merged = new Dictionary<string, string>(baseConfig, StringComparer.Ordinal);
        foreach (var (key, value) in overrideConfig)
            merged[key] = value;
        return merged.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private ResolvedProfile Merge(ResolvedProfile parent, NodeProfile child) {
        var consumers = new Dictionary<string, ResolvedConsumerConfig>(parent.Consumers, StringComparer.Ordinal);

        // 1. 排除子 Profile 明确移除的节点
        //    两种写法：
        //      `node`              → 从所有消费点移除（历史语义，`crash-snapshot-mw` 这类「全环境关掉」用它）
        //      `consumer:node`     → 只从指定消费点移除（TASK100 S7）
        //    为什么需要后者：节点 ID 是全局命名空间，工具组 `web` 与将来任何名为 web 的中间件会互相误伤。
        //    前缀只有在「确实是已存在的消费点」时才当限定符 —— 否则整串仍按节点 ID 处理，
        //    这样即使有人把节点 ID 写成带冒号的样子，也不会被悄悄解释成另一个消费点的排除。
        foreach (var entry in child.Excludes) {
            var (scopedConsumer, nodeId) = SplitExclude(entry, consumers);
            var foundAny = false;
            foreach (var (consumerId, consumerConfig) in consumers) {
                if (scopedConsumer is not null && consumerId != scopedConsumer) continue;
                if (consumerConfig.Nodes.Any(n => n.Id == nodeId)) {
                    foundAny = true;
                    var filtered = consumerConfig.Nodes.Where(n => n.Id != nodeId).ToArray();
                    consumers[consumerId] = consumerConfig with { Nodes = filtered };
                }
            }
            if (!foundAny) {
                if (scopedConsumer is null)
                    _logger?.LogWarning("Profile '{ProfileName}' excludes node '{NodeId}' but it was not found in any consumer", child.Name, nodeId);
                else
                    _logger?.LogWarning("Profile '{ProfileName}' excludes node '{NodeId}' from consumer '{ConsumerId}' but it was not found there", child.Name, nodeId, scopedConsumer);
            }
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
                // 契约随「省略即继承」：子 Profile 未声明则沿用父级契约。
                consumers[consumerId] = existing with {
                    Nodes = existingNodes.Values.OrderBy(n => n.Priority).ToArray(),
                    Contract = config.Contract ?? existing.Contract
                };
            } else {
                var nodes = config.Nodes
                    .Select(n => new ResolvedNodeEntry(n.Id, n.Priority, n.EnabledWhenFeatureFlag, n.Config))
                    .ToArray();
                consumers[consumerId] = new ResolvedConsumerConfig(consumerId, nodes, config.Contract ?? NodeContract.Middleware);
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
