namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具参数名归一化器 — 参数名映射规则的<b>唯一真相</b>。
/// <para>参数名会出现在三条链路上：宿主工具分发（中间件 <c>ArgumentRepairMiddleware</c>）、
/// 独立工具 exe（契约层 <c>CliNodeRunner</c>）、CLI 元命令（<c>jcc tool call</c> / <c>jcc mcp_call</c>）。
/// 三条链路必须给出同一个答案 —— 否则同一次调用在 host 路径被自动修复、在 local 路径被直接拒绝，
/// 调用方（尤其是模型）无从判断该用哪种写法，只能逐个试错。</para>
/// <para><b>为什么下沉到契约层</b>：独立工具 exe 受依赖墙硬约束，只允许引用本程序集
/// （见 <c>modules/tools/cli_nodes/cli_node.props</c>），引用不到 utils 里的实现。
/// 规则留在 utils 就等于 exe 侧只能各写一套 —— 那是「同一决策的两个实现」。</para>
/// </summary>
public static class ToolParameterNameRepairer {
    /// <summary>
    /// 参数别名表 — schema 规范名 → 调用方可能使用的别名。
    /// <para>方向是「规范名 → 别名」；反向查找由 <see cref="Variants"/> 的 snake/camel 变体兜底，
    /// 所以 <c>filePath</c> 与 <c>file_path</c> 双向都能命中。</para>
    /// </summary>
    private static readonly FrozenDictionary<string, string> ParameterAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        ["file_path"] = "filePath",
        ["file_name"] = "fileName",
        ["oldString"] = "old_string",
        ["newString"] = "new_string",
        ["old_text"] = "old_string",
        ["new_text"] = "new_string",
        ["path"] = "filePath",
        ["file"] = "filePath",
        ["directory"] = "dirPath",
        ["dir"] = "dirPath",
        ["search_query"] = "query",
        ["search_pattern"] = "pattern",
        ["search_string"] = "pattern",
        ["regex_pattern"] = "pattern",
        ["line_number"] = "lineNumber",
        ["line_num"] = "lineNumber",
        ["line"] = "lineNumber",
        ["page_num"] = "pageNumber",
        ["page_number"] = "pageNumber",
        ["command_text"] = "command",
        ["cmd"] = "command",
        ["script"] = "command",
        ["url_link"] = "url",
        ["link"] = "url",
        ["uri"] = "url",
        ["web_url"] = "url",
        ["search_term"] = "query",
        ["text_content"] = "content",
        ["body"] = "content",
        ["message_text"] = "message",
        ["msg"] = "message",
        ["explanation_text"] = "explanation",
        ["desc"] = "description",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 修复参数名 — 将调用方发送的别名/大小写不匹配的参数名映射为 schema 中定义的标准名。
    /// <para>策略: 直接匹配 &gt; 别名映射 &gt; OrdinalIgnoreCase &gt; snake_case/camelCase 转换</para>
    /// </summary>
    /// <param name="arguments">原始参数表。</param>
    /// <param name="schema">工具参数表（唯一数据源）。</param>
    /// <returns>修复后的参数表、是否发生修改、修复说明。</returns>
    public static (Dictionary<string, JsonElement> Arguments, bool Modified, string? Hint) RepairParameterNames(
        Dictionary<string, JsonElement> arguments,
        ToolSchema schema) {
        var schemaProps = schema.Properties.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repairs = new List<string>();
        var repaired = new Dictionary<string, JsonElement>(arguments.Count);

        foreach (var (key, value) in arguments) {
            if (schemaProps.Contains(key)) {
                // 使用 OrdinalIgnoreCase HashSet 时，Contains("Pattern") 对 "pattern" 返回 true
                // 但必须用 schema 中的实际 key（"pattern"）存储，否则下游工具按精确匹配找不到参数
                var actualKey = FindActualKey(key, schemaProps) ?? key;
                repaired[actualKey] = value;
                if (!string.Equals(actualKey, key, StringComparison.Ordinal)) {
                    repairs.Add($"'{key}' → '{actualKey}'");
                }
                continue;
            }

            var matched = TryMatchParameter(key, schemaProps);
            if (matched is not null) {
                // 不覆盖已由直接匹配设置的值（直接匹配优先于别名匹配）
                // 场景: schema 有 file_path，LLM 同时发送 file_path(直接匹配) 和 path(别名→filePath→snake_case file_path)
                // 若别名覆盖直接匹配，会导致正确的 file_path 值被丢弃
                if (!repaired.ContainsKey(matched)) {
                    repaired[matched] = value;
                    repairs.Add($"'{key}' → '{matched}'");
                } else {
                    // 目标 key 已被直接匹配占用，保留原 key 避免数据丢失
                    repaired[key] = value;
                }
            } else {
                repaired[key] = value;
            }
        }

        if (repairs.Count == 0)
            return (arguments, false, null);

        return (repaired, true, $"renamed parameter(s): {string.Join(", ", repairs)}");
    }

    /// <summary>
    /// 归一化原始字符串参数名 — 独立工具 exe 侧使用。
    /// <para>exe 侧拿到的是「命令行里的键」与「帮助文本里声明的参数名」两份字符串，
    /// 没有 schema 对象，所以按同一套规则做双向匹配：
    /// 直接命中 &gt; 别名变体 &gt; snake/camel 变体；都不命中就保持原样
    /// （由工具自己报「缺少必填参数」并给出正确写法，不在这里瞎猜）。</para>
    /// </summary>
    /// <param name="rawNames">命令行解析出的原始参数名与值。</param>
    /// <param name="knownNames">该工具声明的参数名集合。</param>
    /// <returns>键已归一化的新字典；键的比较大小写不敏感。</returns>
    public static Dictionary<string, string> RepairNames(
        IReadOnlyDictionary<string, string> rawNames, IReadOnlyCollection<string> knownNames) {
        var repaired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (rawNames.Count == 0) return repaired;
        if (knownNames.Count == 0) {
            foreach (var (key, value) in rawNames) repaired[key] = value;
            return repaired;
        }

        var known = knownNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in rawNames) {
            repaired[ResolveCanonical(key, known) ?? key] = value;
        }
        return repaired;
    }

    /// <summary>把参数名解析成该工具的规范名；无法判定时返回 null。</summary>
    private static string? ResolveCanonical(string key, HashSet<string> known) {
        foreach (var candidate in known) {
            if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        if (ParameterAliases.TryGetValue(key, out var alias)) {
            foreach (var candidate in Variants(alias)) {
                if (known.Contains(candidate)) return FindActualKey(candidate, known);
            }
        }
        foreach (var candidate in Variants(key)) {
            if (known.Contains(candidate)) return FindActualKey(candidate, known);
        }
        return null;
    }

    /// <summary>参数名的 snake_case / camelCase 两种变体。</summary>
    private static IEnumerable<string> Variants(string name) {
        yield return ToSnakeCase(name);
        yield return ToCamelCase(name);
    }

    private static string? TryMatchParameter(string wrongName, HashSet<string> schemaProps) {
        if (ParameterAliases.TryGetValue(wrongName, out var alias)) {
            if (schemaProps.Contains(alias))
                return FindActualKey(alias, schemaProps);

            // 别名目标值不匹配时，尝试 snake_case/camelCase 转换
            // 例: alias="filePath"，schema 属性名为 "file_path"
            var aliasSnake = ToSnakeCase(alias);
            if (schemaProps.Contains(aliasSnake))
                return FindActualKey(aliasSnake, schemaProps);

            var aliasCamel = ToCamelCase(alias);
            if (schemaProps.Contains(aliasCamel))
                return FindActualKey(aliasCamel, schemaProps);
        }

        foreach (var schemaKey in schemaProps) {
            if (string.Equals(wrongName, schemaKey, StringComparison.OrdinalIgnoreCase))
                return schemaKey;
        }

        var snakeCase = ToSnakeCase(wrongName);
        if (schemaProps.Contains(snakeCase))
            return FindActualKey(snakeCase, schemaProps);

        var camelCase = ToCamelCase(wrongName);
        if (schemaProps.Contains(camelCase))
            return FindActualKey(camelCase, schemaProps);

        return null;
    }

    private static string? FindActualKey(string key, HashSet<string> schemaProps) {
        foreach (var schemaKey in schemaProps) {
            if (string.Equals(schemaKey, key, StringComparison.OrdinalIgnoreCase))
                return schemaKey;
        }
        return key;
    }

    /// <summary>camelCase → snake_case。</summary>
    public static string ToSnakeCase(string name) {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++) {
            if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    /// <summary>snake_case → camelCase。</summary>
    public static string ToCamelCase(string name) {
        if (string.IsNullOrEmpty(name)) return name;
        var parts = name.Split('_');
        if (parts.Length <= 1) return name;
        var sb = new StringBuilder(name.Length);
        sb.Append(parts[0].ToLowerInvariant());
        for (var i = 1; i < parts.Length; i++) {
            if (parts[i].Length > 0) {
                sb.Append(char.ToUpperInvariant(parts[i][0]));
                sb.Append(parts[i].Substring(1).ToLowerInvariant());
            }
        }
        return sb.ToString();
    }
}
