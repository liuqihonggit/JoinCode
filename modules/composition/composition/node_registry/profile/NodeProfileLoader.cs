namespace JoinCode.Composition.NodeRegistry;

/// <summary>
/// 从内嵌资源加载 NodeProfile YAML 文件
/// Profile 文件位于 resources/profiles/ 目录，编译时嵌入为 EmbeddedResource
/// </summary>
public static class NodeProfileLoader {
    private const string ResourcePrefix = "JoinCode.Composition.Profiles.";

    /// <summary>
    /// 从程序集内嵌资源加载所有 Profile，并按需叠加外部目录覆盖。
    /// <para>
    /// TASK100 S5：此前 profile 只存在于 EmbeddedResource（<c>Composition.csproj</c> 编译时嵌入），
    /// 改一行节点顺序都要重编译 —— 四问的第④条（不改代码改配置）因此全局不成立。
    /// 现在可通过 <c>JCC_PROFILE_DIR</c> 指定外部目录：<b>同名文件整体覆盖内嵌版本</b>，新名字则新增。
    /// </para>
    /// <para>
    /// 为什么是「整体覆盖」而不是字段级合并：yaml 本身已有 <c>inherits</c> / <c>overrides</c> / <c>excludes</c>
    /// 三个局部改写机制，想要局部改就写个小文件 <c>inherits: base</c>，比隐式的深合并可预测得多。
    /// </para>
    /// </summary>
    /// <param name="assembly">内嵌资源所在程序集（默认当前程序集）。</param>
    /// <param name="fileSystem">读外部目录用的文件系统抽象；不读外部目录时可传 null。</param>
    /// <param name="externalDirectory">外部 profile 目录；为 null 时读环境变量 <c>JCC_PROFILE_DIR</c>。</param>
    /// <returns>profile 名 → 解析结果</returns>
    public static FrozenDictionary<string, NodeProfile> LoadFromResources(
        Assembly? assembly = null,
        IFileSystem? fileSystem = null,
        string? externalDirectory = null) {
        var asm = assembly ?? Assembly.GetExecutingAssembly();
        var profileNames = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..])
            .Select(n => n.EndsWith(".yaml", StringComparison.Ordinal) ? n[..^5] : n)
            .Where(n => n.Length > 0)
            .ToList();

        var profiles = new Dictionary<string, NodeProfile>(StringComparer.Ordinal);
        foreach (var name in profileNames) {
            try {
                var profile = LoadProfile(asm, name);
                if (profile is not null) profiles[name] = profile;
            } catch (Exception ex) {
                throw new InvalidOperationException($"Failed to load profile '{name}': {ex.Message}", ex);
            }
        }

        ApplyExternalOverlay(profiles, fileSystem, externalDirectory ?? Environment.GetEnvironmentVariable(JccEnvVar.NodeProfileDir.ToValue()));
        return profiles.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// 叠加外部 profile 目录 — 同名覆盖、新名新增；目录不存在或文件解析失败一律抛错（fail-closed）。
    /// </summary>
    /// <param name="profiles">已加载的内嵌 profile（就地修改）。</param>
    /// <param name="fileSystem">文件系统抽象（JCC9001：本程序集不得直接触盘）。</param>
    /// <param name="directory">外部目录；空则不做任何事。</param>
    private static void ApplyExternalOverlay(Dictionary<string, NodeProfile> profiles, IFileSystem? fileSystem, string? directory) {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        if (fileSystem is null) {
            throw new InvalidOperationException(
                $"[PROFILE-DIR-NO-FS] 已通过 {JccEnvVar.NodeProfileDir.ToValue()} 指定外部 profile 目录，但当前入口没有 IFileSystem。" +
                "处置: 用宿主入口（含 DI）运行，或去掉该环境变量。");
        }

        if (!fileSystem.DirectoryExists(directory)) {
            throw new InvalidOperationException(
                $"[PROFILE-DIR-NOT-FOUND] 环境变量 {JccEnvVar.NodeProfileDir.ToValue()} 指向的目录不存在: {directory}。" +
                "处置: 修正路径，或删除该环境变量以使用内嵌 profile。");
        }

        var applied = 0;
        foreach (var file in fileSystem.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.Ordinal)) {
            var name = Path.GetFileNameWithoutExtension(file);
            NodeProfile profile;
            try {
                profile = ParseYaml(name, string.Join("\n", fileSystem.ReadAllLines(file)));
            } catch (Exception ex) {
                throw new InvalidOperationException($"[PROFILE-DIR-PARSE] 外部 profile '{file}' 解析失败: {ex.Message}", ex);
            }

            var action = profiles.ContainsKey(name) ? "override" : "add";
            profiles[name] = profile;
            applied++;
            Diag.WriteLine($"[PROFILE] external {action}: {name} <- {file}");
        }

        if (applied == 0) {
            throw new InvalidOperationException(
                $"[PROFILE-DIR-EMPTY] 外部 profile 目录里没有任何 .yaml: {directory}。" +
                "处置: 放入 profile 文件，或删除该环境变量。");
        }
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
        if (yamlStream.Documents[0].RootNode is not YamlMappingNode root) throw new InvalidOperationException($"Profile '{name}' YAML root node must be a mapping, got {yamlStream.Documents[0].RootNode.GetType().Name}");

        var inherits = root.Children.TryGetValue("inherits", out var inheritsNode) ? inheritsNode.ToString() : null;

        var consumers = new Dictionary<string, ConsumerConfig>(StringComparer.Ordinal);
        if (root.Children.TryGetValue("consumers", out var consumersNode)) {
            if (consumersNode is not YamlMappingNode consumersMapping)
                throw new InvalidOperationException($"Profile '{name}': 'consumers' must be a mapping, got {consumersNode.GetType().Name}");
            foreach (var (key, value) in consumersMapping.Children) {
                var consumerId = key.ToString() ?? "";
                if (string.IsNullOrEmpty(consumerId))
                    throw new InvalidOperationException($"Profile '{name}': consumer has empty key");
                var nodes = ParseConsumerNodes(value, name);
                consumers[consumerId] = new ConsumerConfig(consumerId, nodes.ToArray(), ParseContract(value, name, consumerId));
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
                    ? int.TryParse(p.ToString(), out var pv) ? pv : throw new InvalidOperationException($"Profile '{name}': override for consumer '{consumer}', node '{node}' has invalid priority value '{p}'")
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
                if (string.IsNullOrEmpty(consumerId))
                    throw new InvalidOperationException($"Profile '{name}': include consumer has empty key");
                var nodes = ParseConsumerNodes(value, name);
                includeDict[consumerId] = new ConsumerConfig(consumerId, nodes.ToArray(), ParseContract(value, name, consumerId));
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
    private static List<NodeEntryConfig> ParseConsumerNodes(YamlNode value, string profileName) {
        if (value is YamlMappingNode mapping) {
            if (mapping.Children.TryGetValue("nodes", out var nodesNode))
                return ParseNodeList(nodesNode, profileName);
            return new List<NodeEntryConfig>();
        }
        return ParseNodeList(value, profileName);
    }

    /// <summary>
    /// 解析消费点的装配契约 — 未声明返回 <see langword="null"/>（交由继承链决定），
    /// 非法值直接报错并列出合法清单，避免拼错后静默降级为 middleware。
    /// </summary>
    /// <param name="value">消费点 YAML 节点（mapping 或简写序列）。</param>
    /// <param name="profileName">Profile 名（错误提示用）。</param>
    /// <param name="consumerId">消费点 ID（错误提示用）。</param>
    /// <returns>规范化后的契约值；未声明返回 <see langword="null"/>。</returns>
    private static string? ParseContract(YamlNode value, string profileName, string consumerId) {
        if (value is not YamlMappingNode mapping) return null;
        if (!mapping.Children.TryGetValue("contract", out var contractNode)) return null;

        var raw = contractNode.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                $"Profile '{profileName}': 消费点 '{consumerId}' 的 contract 为空。合法值: {string.Join(", ", NodeContract.All)}");

        if (!NodeContract.IsValid(raw))
            throw new InvalidOperationException(
                $"Profile '{profileName}': 消费点 '{consumerId}' 的 contract '{raw}' 非法。" +
                $"合法值: {string.Join(", ", NodeContract.All)}。" +
                $"正确写法: consumers.{consumerId}.contract: {NodeContract.Middleware}|{NodeContract.Guard}|{NodeContract.Provider}|{NodeContract.Role}|{NodeContract.Capability}|{NodeContract.CommandGroup}");

        return NodeContract.Normalize(raw);
    }

    private static List<NodeEntryConfig> ParseNodeList(YamlNode node, string profileName) {
        var result = new List<NodeEntryConfig>();
        if (node is not YamlSequenceNode seq) return result;

        foreach (var item in seq.Children) {
            if (item is not YamlMappingNode map) continue;
            var id = map.Children.TryGetValue("id", out var idNode) ? idNode.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException($"Profile '{profileName}': node entry has empty or missing 'id' field");
            var priority = map.Children.TryGetValue("priority", out var priNode)
                ? int.TryParse(priNode.ToString(), out var p) ? p : throw new InvalidOperationException($"Profile '{profileName}': node '{id}' has invalid priority value '{priNode}'")
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
