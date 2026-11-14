namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 从内嵌资源加载 NodeProfile YAML 文件
/// Profile 文件位于 resources/profiles/ 目录，编译时嵌入为 EmbeddedResource
/// </summary>
public static class NodeProfileLoader {
    private const string ResourcePrefix = "JoinCode.Composition.Profiles.";

    /// <summary>从程序集内嵌资源加载所有 Profile</summary>
    public static FrozenDictionary<string, NodeProfile> LoadFromResources(Assembly? assembly = null) {
        var asm = assembly ?? Assembly.GetExecutingAssembly();
        var profileNames = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..])
            .Select(n => n.EndsWith(".yaml", StringComparison.Ordinal) ? n[..^5] : n)
            .ToList();

        var profiles = new Dictionary<string, NodeProfile>(StringComparer.Ordinal);
        foreach (var name in profileNames) {
            var profile = LoadProfile(asm, name);
            if (profile is not null) profiles[name] = profile;
        }
        return profiles.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>加载单个 Profile</summary>
    public static NodeProfile? LoadProfile(Assembly asm, string name) {
        var resourceName = $"{ResourcePrefix}{name}.yaml";
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        var yaml = reader.ReadToEnd();
        return ParseYaml(name, yaml);
    }

    /// <summary>解析 YAML 文本为 NodeProfile</summary>
    public static NodeProfile ParseYaml(string name, string yaml) {
        var yamlStream = new YamlStream();
        yamlStream.Load(new StringReader(yaml));

        if (yamlStream.Documents.Count == 0) throw new InvalidOperationException($"Profile '{name}' YAML 为空");
        if (yamlStream.Documents[0].RootNode is not YamlMappingNode root) throw new InvalidOperationException($"Profile '{name}' YAML 根节点不是 mapping");

        var inherits = root.Children.TryGetValue("inherits", out var inheritsNode) ? inheritsNode.ToString() : null;

        var consumers = new Dictionary<string, ConsumerConfig>(StringComparer.Ordinal);
        if (root.Children.TryGetValue("consumers", out var consumersNode) && consumersNode is YamlMappingNode consumersMapping) {
            foreach (var (key, value) in consumersMapping.Children) {
                var consumerId = key.ToString() ?? "";
                var nodes = ParseConsumerNodes(value);
                consumers[consumerId] = new ConsumerConfig(consumerId, nodes.ToArray());
            }
        }

        var excludes = FrozenSet<string>.Empty;
        if (root.Children.TryGetValue("excludes", out var excludesNode) && excludesNode is YamlSequenceNode excludesSeq) {
            excludes = excludesSeq.Children.Select(n => n.ToString() ?? "").ToFrozenSet(StringComparer.Ordinal);
        }

        var overrides = Array.Empty<NodeProfileOverride>();
        if (root.Children.TryGetValue("overrides", out var overridesNode) && overridesNode is YamlSequenceNode overridesSeq) {
            var overrideList = new List<NodeProfileOverride>();
            foreach (var item in overridesSeq.Children) {
                if (item is not YamlMappingNode overrideMap) continue;
                var consumer = overrideMap.Children.TryGetValue("consumer", out var c) ? c.ToString() ?? "" : "";
                var node = overrideMap.Children.TryGetValue("node", out var n) ? n.ToString() ?? "" : "";
                var priority = overrideMap.Children.TryGetValue("priority", out var p)
                    ? int.TryParse(p.ToString(), out var pv) ? pv : (int?)null
                    : (int?)null;
                var config = overrideMap.Children.TryGetValue("config", out var cfg) && cfg is YamlMappingNode cfgMap
                    ? cfgMap.Children.ToFrozenDictionary(k => k.Key.ToString() ?? "", v => v.Value.ToString() ?? "", StringComparer.Ordinal)
                    : null;
                overrideList.Add(new NodeProfileOverride(consumer, node, priority, config));
            }
            overrides = overrideList.ToArray();
        }

        var includes = FrozenDictionary<string, ConsumerConfig>.Empty;
        if (root.Children.TryGetValue("includes", out var includesNode) && includesNode is YamlMappingNode includesMap) {
            var includeDict = new Dictionary<string, ConsumerConfig>(StringComparer.Ordinal);
            foreach (var (key, value) in includesMap.Children) {
                var consumerId = key.ToString() ?? "";
                var nodes = ParseConsumerNodes(value);
                includeDict[consumerId] = new ConsumerConfig(consumerId, nodes.ToArray());
            }
            includes = includeDict.ToFrozenDictionary(StringComparer.Ordinal);
        }

        return new NodeProfile(
            Name: name,
            Inherits: inherits,
            Consumers: consumers.ToFrozenDictionary(StringComparer.Ordinal),
            Excludes: excludes,
            Overrides: overrides,
            Includes: includes
        );
    }

    /// <summary>
    /// 解析消费点的节点列表 — 支持两种 YAML 格式：
    /// 1. { nodes: [...] } — mapping 包含 nodes 字段（标准格式）
    /// 2. [...] — 直接是序列（简写格式）
    /// </summary>
    private static List<NodeEntryConfig> ParseConsumerNodes(YamlNode value) {
        if (value is YamlMappingNode mapping) {
            if (mapping.Children.TryGetValue("nodes", out var nodesNode))
                return ParseNodeList(nodesNode);
            return new List<NodeEntryConfig>();
        }
        return ParseNodeList(value);
    }

    private static List<NodeEntryConfig> ParseNodeList(YamlNode node) {
        var result = new List<NodeEntryConfig>();
        if (node is not YamlSequenceNode seq) return result;

        foreach (var item in seq.Children) {
            if (item is not YamlMappingNode map) continue;
            var id = map.Children.TryGetValue("id", out var idNode) ? idNode.ToString() ?? "" : "";
            var priority = map.Children.TryGetValue("priority", out var priNode)
                ? int.TryParse(priNode.ToString(), out var p) ? p : 0
                : 0;
            var enabledWhen = map.Children.TryGetValue("enabled-when", out var enNode) ? enNode.ToString() : null;
            var config = map.Children.TryGetValue("config", out var cfgNode) && cfgNode is YamlMappingNode cfgMap
                ? cfgMap.Children.ToFrozenDictionary(k => k.Key.ToString() ?? "", v => v.Value.ToString() ?? "", StringComparer.Ordinal)
                : FrozenDictionary<string, string>.Empty;
            result.Add(new NodeEntryConfig(id, priority, enabledWhen, config));
        }
        return result;
    }
}
