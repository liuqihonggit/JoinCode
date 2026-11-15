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
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 存在缺少 id 的节点");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 节点 '{id}' 缺少 name");
        if (string.IsNullOrWhiteSpace(summary))
            throw new InvalidOperationException($"CLI 节点清单 '{source}' 节点 '{id}' 缺少 summary");

        return new CliToolDescriptor {
            Id = id,
            Name = name,
            Summary = summary,
            Category = ReadScalar(map, "category") ?? "general",
            Exe = ReadScalar(map, "exe"),
            Tags = ReadStringArray(map, "tags"),
            Examples = ReadStringArray(map, "examples"),
            Parameters = ReadSequence(map, "parameters")
                .OfType<YamlMappingNode>()
                .Select(param => ParseParameter(param, source, id))
                .ToArray()
        };
    }

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
