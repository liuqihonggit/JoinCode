namespace CliPermission.Analyzer;

/// <summary>
/// 权限 YAML 的编译期轻量索引 — 只提取工具、参数名及其位置。
/// <para>完整 schema 由运行时 CliToolPolicyLoader 严格解析；分析器不加载运行时程序集，
/// 避免 analyzer 依赖 YamlDotNet 在编译器加载上下文中丢失。</para>
/// </summary>
internal sealed class PolicyIndex {
    private static readonly StringComparer NameComparer = StringComparer.Ordinal;

    private PolicyIndex(string policyId, Dictionary<string, PolicyToolEntry> tools) {
        PolicyId = policyId;
        Tools = tools;
    }

    public string PolicyId { get; }

    public Dictionary<string, PolicyToolEntry> Tools { get; }

    public bool IsEmpty => Tools.Count == 0;

    public static PolicyIndex Load(IEnumerable<AdditionalText> files, CancellationToken cancellationToken) {
        var tools = new Dictionary<string, PolicyToolEntry>(NameComparer);
        var policyId = "<unknown>";
        foreach (var file in files) {
            cancellationToken.ThrowIfCancellationRequested();
            var text = file.GetText(cancellationToken);
            if (text is null) continue;
            var content = text.ToString();
            if (content.IndexOf("policy_id:", StringComparison.Ordinal) < 0) continue;

            var parsed = Parse(file.Path, text);
            if (parsed is null) continue;
            if (!string.IsNullOrWhiteSpace(parsed.PolicyId)) policyId = parsed.PolicyId;
            foreach (var pair in parsed.Tools) {
                if (!tools.ContainsKey(pair.Key)) tools.Add(pair.Key, pair.Value);
            }
        }
        return new PolicyIndex(policyId, tools);
    }

    private static PolicyIndex? Parse(string path, SourceText text) {
        string? policyId = null;
        var tools = new Dictionary<string, PolicyToolEntry>(NameComparer);
        PolicyToolEntry? currentTool = null;
        PolicyParameterEntry? currentParameter = null;
        var inTools = false;
        var inParameters = false;
        var baseIndent = -1;

        foreach (var line in text.Lines) {
            var raw = line.ToString();
            var trimmed = raw.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

            var absoluteIndent = raw.Length - trimmed.Length;
            if (baseIndent < 0) baseIndent = absoluteIndent;
            var indent = absoluteIndent - baseIndent;
            if (indent == 0) {
                inParameters = false;
                currentTool = null;
                currentParameter = null;
                inTools = string.Equals(trimmed, "tools:", StringComparison.Ordinal);
                if (trimmed.StartsWith("policy_id:", StringComparison.Ordinal)) {
                    policyId = trimmed.Substring("policy_id:".Length).Trim();
                }
                continue;
            }

            if (!inTools) continue;

            if (indent == 2 && trimmed.EndsWith(":", StringComparison.Ordinal)) {
                var toolId = Unquote(trimmed.Substring(0, trimmed.Length - 1).Trim());
                if (toolId.Length == 0) continue;
                currentTool = new PolicyToolEntry(toolId, CreateLineLocation(path, line));
                if (!tools.ContainsKey(toolId)) tools.Add(toolId, currentTool);
                inParameters = false;
                currentParameter = null;
                continue;
            }

            if (currentTool is null) continue;

            if (indent == 4 && trimmed.StartsWith("parameters:", StringComparison.Ordinal)) {
                inParameters = true;
                currentParameter = null;
                continue;
            }

            if (inParameters && indent == 6 && trimmed.EndsWith(":", StringComparison.Ordinal)) {
                var parameterName = Unquote(trimmed.Substring(0, trimmed.Length - 1).Trim());
                if (parameterName.Length > 0 && !currentTool.Parameters.ContainsKey(parameterName)) {
                    currentParameter = new PolicyParameterEntry(parameterName, CreateLineLocation(path, line));
                    currentTool.Parameters.Add(parameterName, currentParameter);
                }
                continue;
            }

            if (currentParameter is not null
                && indent == 8
                && trimmed.StartsWith("effect:", StringComparison.Ordinal)) {
                currentParameter.Effect = ParseEffect(StripInlineComment(trimmed.Substring("effect:".Length)));
            }
        }

        return policyId is null ? null : new PolicyIndex(policyId, tools);
    }

    private static Location CreateLineLocation(string path, TextLine line) =>
        Location.Create(
            path,
            line.Span,
            new LinePositionSpan(
                new LinePosition(line.LineNumber, 0),
                new LinePosition(line.LineNumber, line.End - line.Start)));

    /// <summary>去掉 YAML 标量两侧引号 — slash 组的工具 ID 形如 <c>"/color"</c>。</summary>
    private static string Unquote(string raw) {
        if (raw.Length >= 2
            && ((raw[0] == '"' && raw[raw.Length - 1] == '"')
                || (raw[0] == '\'' && raw[raw.Length - 1] == '\''))) {
            return raw.Substring(1, raw.Length - 2);
        }
        return raw;
    }

    /// <summary>去掉 YAML 行尾注释 — <c>effect: deny  # 说明</c> 必须解析为 deny。</summary>
    private static string StripInlineComment(string raw) {
        var value = raw.Trim();
        var hash = value.IndexOf('#');
        return hash >= 0 ? value.Substring(0, hash).Trim() : value;
    }

    private static PolicyEffect ParseEffect(string raw) => raw switch {
        "allow" => PolicyEffect.Allow,
        "ask" => PolicyEffect.Ask,
        "deny" => PolicyEffect.Deny,
        _ => PolicyEffect.Unknown
    };
}

/// <summary>编译期只关心的策略效果。</summary>
internal enum PolicyEffect {
    Unknown,
    Allow,
    Ask,
    Deny
}

/// <summary>编译期策略中的一个工具及其参数位置。</summary>
internal sealed class PolicyToolEntry {
    public PolicyToolEntry(string toolId, Location location) {
        ToolId = toolId;
        Location = location;
        Parameters = new Dictionary<string, PolicyParameterEntry>(StringComparer.Ordinal);
    }

    public string ToolId { get; }

    public Location Location { get; }

    public Dictionary<string, PolicyParameterEntry> Parameters { get; }
}

/// <summary>编译期策略中的一个参数；deny 条目可以只为不支持/禁用的参数立墓碑。</summary>
internal sealed class PolicyParameterEntry {
    public PolicyParameterEntry(string name, Location location) {
        Name = name;
        Location = location;
    }

    public string Name { get; }

    public Location Location { get; }

    public PolicyEffect Effect { get; set; } = PolicyEffect.Unknown;
}
