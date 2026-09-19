namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 参数名修复器 — 从 ToolCallRepairService 提取的单一职责小类
/// <para>职责: 参数别名映射 + snake_case/camelCase 转换 + 参数名归一化</para>
/// </summary>
internal static class ParameterNameRepairer {
    private static readonly FrozenDictionary<string, string> ParameterAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        ["file_path"] = "filePath",
        ["file_name"] = "fileName",
        ["old_string"] = "old_string",
        ["new_string"] = "new_string",
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
    /// 修复参数名 — 将 LLM 发送的别名/大小写不匹配的参数名映射为 schema 中定义的标准名
    /// <para>策略: 直接匹配 > 别名映射 > OrdinalIgnoreCase > snake_case/camelCase 转换</para>
    /// </summary>
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

    private static string ToSnakeCase(string name) {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++) {
            if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    private static string ToCamelCase(string name) {
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