namespace JoinCode.Abstractions.Tools;

/// <summary>
/// CLI 工具权限策略加载入口 — 严格解析 YAML，拒绝缺字段、未知字段与未知能力。
/// <para>AOT 兼容：使用 <see cref="YamlStream"/> 手工读取节点，不使用反射式反序列化。</para>
/// </summary>
public static class CliToolPolicyLoader {
    private const string SupportedSchemaVersion = "1";

    private static readonly FrozenSet<string> RootKeys = new[] {
        "schema_version", "policy_id", "policy_version", "executables", "tools"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ToolKeys = new[] {
        "effect", "parameters"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ParameterKeys = new[] {
        "effect", "capabilities"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>解析一份权限策略 YAML。</summary>
    /// <param name="yaml">YAML 文本。</param>
    /// <param name="source">来源标识，用于错误定位。</param>
    /// <returns>严格校验后的策略清单。</returns>
    public static CliToolPolicyManifest Parse(string yaml, string source = "<text>") {
        ArgumentNullException.ThrowIfNull(yaml);
        var root = ParseRoot(yaml, source);
        EnsureKnownKeys(root, RootKeys, source, "根节点");

        var schemaVersion = RequireScalar(root, "schema_version", source, "根节点");
        if (!string.Equals(schemaVersion, SupportedSchemaVersion, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的 schema_version 只支持 {SupportedSchemaVersion}，实际为 '{schemaVersion}'");
        }

        var policyId = RequireScalar(root, "policy_id", source, "根节点");
        var policyVersion = ParsePositiveInt(
            RequireScalar(root, "policy_version", source, "根节点"),
            source, "policy_version");

        var executables = ReadExecutables(root, source);
        var tools = ReadTools(root, source);

        return new CliToolPolicyManifest {
            SchemaVersion = schemaVersion,
            PolicyId = policyId,
            PolicyVersion = policyVersion,
            Executables = executables,
            Tools = tools
        };
    }

    private static IReadOnlyList<string> ReadExecutables(YamlMappingNode root, string source) {
        if (!root.Children.TryGetValue(new YamlScalarNode("executables"), out var node)
            || node is not YamlSequenceNode sequence) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的 executables 必须是列表，且至少包含一个可执行文件名");
        }

        var executables = new List<string>();
        foreach (var child in sequence.Children) {
            if (child is not YamlScalarNode { Value: { } value } || string.IsNullOrWhiteSpace(value)) {
                throw new InvalidOperationException(
                    $"CLI 权限策略 '{source}' 的 executables 存在空或非字符串条目");
            }
            executables.Add(value);
        }
        if (executables.Count == 0) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的 executables 必须是列表，且至少包含一个可执行文件名");
        }
        return executables;
    }

    private static IReadOnlyList<CliToolPolicy> ReadTools(YamlMappingNode root, string source) {
        var toolsMap = RequireMapping(root, "tools", source, "根节点");
        var tools = new List<CliToolPolicy>(toolsMap.Children.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (keyNode, valueNode) in toolsMap.Children) {
            var toolId = ScalarKey(keyNode, source, "tools");
            if (!seen.Add(toolId)) {
                throw new InvalidOperationException(
                    $"CLI 权限策略 '{source}' 的工具 '{toolId}' 重复声明");
            }
            if (valueNode is not YamlMappingNode toolMap) {
                throw new InvalidOperationException(
                    $"CLI 权限策略 '{source}' 的工具 '{toolId}' 必须是 mapping");
            }
            EnsureKnownKeys(toolMap, ToolKeys, source, $"工具 '{toolId}'");
            var effect = ParseEffect(toolMap, "effect", source, $"工具 '{toolId}'");
            var parameters = ReadParameters(toolMap, source, toolId);
            tools.Add(new CliToolPolicy {
                ToolId = toolId,
                Effect = effect,
                Parameters = parameters
            });
        }

        return tools;
    }

    private static IReadOnlyList<CliToolParameterPolicy> ReadParameters(
        YamlMappingNode toolMap, string source, string toolId) {
        if (!toolMap.Children.TryGetValue(new YamlScalarNode("parameters"), out var node)) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的工具 '{toolId}' 缺少 parameters；无参数工具必须显式写 parameters: []");
        }

        if (node is YamlSequenceNode { Children.Count: 0 }) {
            return [];
        }
        if (node is not YamlMappingNode parametersMap) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的工具 '{toolId}' 的 parameters 必须是 mapping 或空列表");
        }

        var parameters = new List<CliToolParameterPolicy>(parametersMap.Children.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (keyNode, valueNode) in parametersMap.Children) {
            var parameterName = ScalarKey(keyNode, source, $"工具 '{toolId}' 的 parameters");
            if (!seen.Add(parameterName)) {
                throw new InvalidOperationException(
                    $"CLI 权限策略 '{source}' 的工具 '{toolId}' 的参数 '{parameterName}' 重复声明");
            }
            if (valueNode is not YamlMappingNode parameterMap) {
                throw new InvalidOperationException(
                    $"CLI 权限策略 '{source}' 的工具 '{toolId}' 的参数 '{parameterName}' 必须是 mapping");
            }
            EnsureKnownKeys(parameterMap, ParameterKeys, source, $"工具 '{toolId}' 的参数 '{parameterName}'");
            var effect = ParseEffect(parameterMap, "effect", source, $"工具 '{toolId}' 的参数 '{parameterName}'");
            parameters.Add(new CliToolParameterPolicy {
                Name = parameterName,
                Effect = effect,
                Capabilities = ReadCapabilities(parameterMap, source, toolId, parameterName)
            });
        }
        return parameters;
    }

    private static IReadOnlyList<CliPolicyCapability> ReadCapabilities(
        YamlMappingNode parameterMap, string source, string toolId, string parameterName) {
        if (!parameterMap.Children.TryGetValue(new YamlScalarNode("capabilities"), out var node)) {
            return [];
        }
        if (node is not YamlSequenceNode sequence) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的工具 '{toolId}' 的参数 '{parameterName}' 的 capabilities 必须是列表");
        }

        var capabilities = new List<CliPolicyCapability>(sequence.Children.Count);
        foreach (var child in sequence.Children) {
            var raw = child is YamlScalarNode { Value: { } value } ? value : null;
            if (string.IsNullOrWhiteSpace(raw) || !CliPolicyCapabilityExtensions.TryParse(raw, out var capability)) {
                var allowed = string.Join(", ", CliPolicyCapabilityExtensions.All.Select(value => value.ToValue()));
                throw new InvalidOperationException(
                    $"CLI 权限策略 '{source}' 的工具 '{toolId}' 的参数 '{parameterName}' 声明了未知能力 '{raw}'；允许值: {allowed}");
            }
            if (!capabilities.Contains(capability)) capabilities.Add(capability);
        }
        return capabilities;
    }

    private static CliPolicyEffect ParseEffect(
        YamlMappingNode map, string key, string source, string owner) {
        if (!map.Children.TryGetValue(new YamlScalarNode(key), out var node)
            || node is not YamlScalarNode { Value: { } raw }) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的{owner}缺少 {key}（必须是 allow/ask/deny）");
        }

        return raw.Trim().ToLowerInvariant() switch {
            "allow" => CliPolicyEffect.Allow,
            "ask" => CliPolicyEffect.Ask,
            "deny" => CliPolicyEffect.Deny,
            _ => throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的{owner}的 {key} '{raw}' 无效（必须是 allow/ask/deny）")
        };
    }

    private static int ParsePositiveInt(string raw, string source, string key) {
        return int.TryParse(raw, out var value) && value > 0
            ? value
            : throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的 {key} 必须是正整数，实际为 '{raw}'");
    }

    private static YamlMappingNode RequireMapping(
        YamlMappingNode parent, string key, string source, string owner) {
        if (!parent.Children.TryGetValue(new YamlScalarNode(key), out var node)
            || node is not YamlMappingNode map) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的{owner}缺少 mapping 字段 '{key}'");
        }
        return map;
    }

    private static string RequireScalar(
        YamlMappingNode parent, string key, string source, string owner) {
        if (!parent.Children.TryGetValue(new YamlScalarNode(key), out var node)
            || node is not YamlScalarNode { Value: { } value }
            || string.IsNullOrWhiteSpace(value)) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的{owner}缺少非空标量字段 '{key}'");
        }
        return value;
    }

    private static string ScalarKey(YamlNode node, string source, string owner) {
        if (node is not YamlScalarNode { Value: { } value } || string.IsNullOrWhiteSpace(value)) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' 的{owner}存在空或非字符串键");
        }
        return value;
    }

    private static void EnsureKnownKeys(
        YamlMappingNode map, FrozenSet<string> allowed, string source, string owner) {
        foreach (var keyNode in map.Children.Keys) {
            if (keyNode is not YamlScalarNode { Value: { } key }) {
                throw new InvalidOperationException(
                    $"CLI 权限策略 '{source}' 的{owner}存在非字符串字段名");
            }
            if (!allowed.Contains(key)) {
                throw new InvalidOperationException(
                    $"CLI 权限策略 '{source}' 的{owner}包含未知字段 '{key}'；允许值: {string.Join(", ", allowed.Order())}");
            }
        }
    }

    private static YamlMappingNode ParseRoot(string yaml, string source) {
        var stream = new YamlStream();
        try {
            stream.Load(new StringReader(yaml));
        } catch (Exception ex) when (ex is YamlException or ArgumentException) {
            throw new InvalidOperationException(
                $"CLI 权限策略 '{source}' YAML 解析失败（可能存在重复键）: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0) {
            throw new InvalidOperationException($"CLI 权限策略 '{source}' 为空");
        }
        if (stream.Documents[0].RootNode is not YamlMappingNode root) {
            throw new InvalidOperationException($"CLI 权限策略 '{source}' 的根节点必须是 mapping");
        }
        return root;
    }
}
