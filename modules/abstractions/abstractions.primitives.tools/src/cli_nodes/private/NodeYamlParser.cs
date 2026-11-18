using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace JoinCode.Abstractions.Tools;

/// <summary>
/// node.yaml / base.yaml 的 AOT 兼容解析器。
/// 刻意使用 YamlStream 手动读节点，而非 Deserializer 反射绑定 — 与 NodeProfileLoader 一致，NativeAOT 下不会被裁剪。
/// </summary>
internal static class NodeYamlParser {
    private const string RootMustBeMapping = "YAML 根节点必须是 mapping";

    internal static BaseToolManifest ParseBase(string yaml, string source) {
        var root = ParseRoot(yaml, source);
        var version = ReadScalar(root, "version") ?? "1";
        var tools = ReadSequence(root, "tools")
            .OfType<YamlMappingNode>()
            .Select(map => ParseDescriptor(map, source))
            .ToArray();
        if (tools.Length == 0)
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 未定义任何基础工具");
        if (tools.Any(t => !t.IsBaseTool))
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 的基础工具不得声明 exe 字段");
        return new BaseToolManifest { Version = version, Tools = tools };
    }

    internal static CliToolDescriptor ParseNode(string yaml, string source) =>
        ParseDescriptor(ParseRoot(yaml, source), source);

    /// <summary>
    /// 解析 cli_groups.yaml —— 首轮提示词的分组索引。
    /// <para>每个条目缺 id/exe/blurb 都直接失败：提示词里出现一个没有说明的 exe，
    /// 等于让模型对着一个不知道用途的命令行猜。</para>
    /// </summary>
    internal static IReadOnlyList<CliGroupEntry> ParseGroupIndex(string yaml, string source) {
        var root = ParseRoot(yaml, source);
        var entries = ReadSequence(root, "groups").OfType<YamlMappingNode>().Select(map => {
            var id = ReadScalar(map, "id");
            var exe = ReadScalar(map, "exe");
            var blurb = ReadScalar(map, "blurb");
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException($"分组索引 '{source}' 存在缺少 id 的条目");
            if (string.IsNullOrWhiteSpace(exe))
                throw new InvalidOperationException($"分组索引 '{source}' 分组 '{id}' 缺少 exe");
            if (string.IsNullOrWhiteSpace(blurb))
                throw new InvalidOperationException($"分组索引 '{source}' 分组 '{id}' 缺少 blurb（提示词里会出现无名 exe）");
            return new CliGroupEntry {
                Id = id,
                Exe = exe,
                Blurb = blurb,
                ToolCount = ReadInt(map, "tools", source, id),
                LocalCount = ReadInt(map, "local", source, id),
                DelegateCount = ReadInt(map, "delegate", source, id)
            };
        }).ToArray();

        if (entries.Length == 0)
            throw new InvalidOperationException($"分组索引 '{source}' 未定义任何分组");
        return entries;
    }

    private static int ReadInt(YamlMappingNode map, string key, string source, string id) {
        var raw = ReadScalar(map, key);
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        return int.TryParse(raw, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"分组索引 '{source}' 分组 '{id}' 的 {key} 必须是整数，实际为 '{raw}'");
    }

    private static YamlMappingNode ParseRoot(string yaml, string source) {
        var stream = new YamlStream();
        try {
            stream.Load(new StringReader(yaml));
        } catch (YamlException ex) {
            throw new InvalidOperationException($"CLI 节点清单 '{source}' YAML 解析失败: {ex.Message}", ex);
        }
        if (stream.Documents.Count == 0)
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 为空");
        if (stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException($"CLI 节点清单 '{source}' {RootMustBeMapping}");
        return root;
    }

    private static CliToolDescriptor ParseDescriptor(YamlMappingNode map, string source) {
        var id = ReadScalar(map, "id");
        var name = ReadScalar(map, "name");
        var summary = ReadScalar(map, "summary");
        var inherits = ReadScalar(map, "inherits");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 存在缺少 id 的节点");
        // G2「省略即继承」：声明 inherits 的子节点只写差异，name/summary 可交给 CliNodeInheritanceResolver 从父继承。
        // 不声明 inherits 时仍强制必填 —— 提示词里出现一个没有说明的 exe，等于让模型对着未知命令行猜。
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(inherits))
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 节点 '{id}' 缺少 name");
        if (string.IsNullOrWhiteSpace(summary) && string.IsNullOrWhiteSpace(inherits))
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 节点 '{id}' 缺少 summary");

        var exe = ReadScalar(map, "exe");
        return new CliToolDescriptor {
            Id = id,
            Name = name ?? "",
            Summary = summary ?? "",
            Category = ReadScalar(map, "category") ?? "general",
            Inherits = inherits,
            Exe = exe,
            Tags = ReadStringArray(map, "tags"),
            Examples = ReadStringArray(map, "examples"),
            Parameters = ReadSequence(map, "parameters")
                .OfType<YamlMappingNode>()
                .Select(param => ParseParameter(param, source, id))
                .ToArray(),
            // 子工具清单只对分组节点（声明了 exe）解析：
            // base.yaml 的 tools 是完整工具描述，形状不同，靠 exe 为空这条既有不变量天然区分。
            Tools = exe is null
                ? []
                : ReadSequence(map, "tools")
                    .OfType<YamlMappingNode>()
                    .Select(entry => ParseToolEntry(entry, source, id))
                    .ToArray(),
            DeclaredKeys = map.Children.Keys
                .OfType<YamlScalarNode>()
                .Select(key => key.Value)
                .Where(key => key is not null)
                .Select(key => key!)
                .ToHashSet(StringComparer.Ordinal)
        };
    }

    private static CliToolEntry ParseToolEntry(YamlMappingNode map, string source, string nodeId) {
        var toolId = ReadScalar(map, "id");
        if (string.IsNullOrWhiteSpace(toolId))
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 节点 '{nodeId}' 存在缺少 id 的子工具条目");
        var rawMode = ReadScalar(map, "mode");
        if (string.IsNullOrWhiteSpace(rawMode))
            throw new InvalidOperationException(
                $"CLI 节点清单 '{source}' 子工具 '{toolId}' 缺少 mode（local/delegate/host）——" +
                "没有执行方式就无法判断该工具是否真正进程隔离，禁止省略");
        return new CliToolEntry {
            Id = toolId,
            Mode = ParseMode(rawMode, source, toolId),
            Aliases = ReadStringArray(map, "aliases")
        };
    }

    private static CliExecutionMode ParseMode(string raw, string source, string toolId) => raw.Trim().ToLowerInvariant() switch {
        "local" => CliExecutionMode.Local,
        "delegate" => CliExecutionMode.Delegate,
        "host" => CliExecutionMode.Host,
        _ => throw new InvalidOperationException(
            $"CLI 节点清单 '{source}' 子工具 '{toolId}' 的 mode 只能是 local / delegate / host，实际为 '{raw}'")
    };

    private static CliToolParameter ParseParameter(YamlMappingNode map, string source, string nodeId) {
        var paramName = ReadScalar(map, "name");
        if (string.IsNullOrWhiteSpace(paramName))
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 节点 '{nodeId}' 存在缺少 name 的参数");
        return new CliToolParameter {
            Name = paramName,
            Type = ReadScalar(map, "type") ?? "string",
            Description = ReadScalar(map, "description"),
            Required = ReadBool(map, "required", fallback: false),
            Default = ReadScalar(map, "default"),
            Aliases = ReadScalar(map, "aliases"),
            Enum = ReadScalar(map, "enum")
        };
    }

    private static string? ReadScalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(key, out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private static bool ReadBool(YamlMappingNode map, string key, bool fallback) {
        var raw = ReadScalar(map, key);
        if (raw is null) return fallback;
        return bool.TryParse(raw, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"YAML 字段 '{key}' 必须是 true/false，实际为 '{raw}'");
    }

    private static IEnumerable<YamlNode> ReadSequence(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(key, out var node) && node is YamlSequenceNode seq
            ? seq.Children
            : Array.Empty<YamlNode>();

    private static string[] ReadStringArray(YamlMappingNode map, string key) {
        var node = map.Children.TryGetValue(key, out var found) ? found : null;
        return node switch {
            YamlSequenceNode seq => seq.Children.Select(child => child.ToString()).ToArray(),
            YamlScalarNode scalar when !string.IsNullOrWhiteSpace(scalar.Value) =>
                scalar.Value!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            _ => []
        };
    }
}
