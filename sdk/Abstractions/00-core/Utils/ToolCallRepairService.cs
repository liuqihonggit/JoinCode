namespace JoinCode.Abstractions.Utils;

public sealed class ToolCallRepairResult
{
    public required bool Success { get; init; }
    public required string RepairedJson { get; init; }
    public string? RepairHint { get; init; }
}

public sealed class ArgumentRepairResult
{
    public required Dictionary<string, JsonElement> RepairedArguments { get; init; }
    public string? RepairHint { get; init; }
}

public static class ToolCallRepairService
{
    private static readonly FrozenDictionary<string, string> ParameterAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
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

    public static ToolCallRepairResult RepairJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new ToolCallRepairResult { Success = true, RepairedJson = "{}" };

        var json = rawJson.Trim();

        if (TryParseJson(json, out _))
            return new ToolCallRepairResult { Success = true, RepairedJson = json };

        var hints = new List<string>();
        var repaired = json;

        repaired = RemoveTrailingCommas(repaired, hints);
        repaired = FixUnquotedKeys(repaired, hints);
        repaired = FixSingleQuotedKeys(repaired, hints);

        if (TryParseJson(repaired, out _))
            return new ToolCallRepairResult
            {
                Success = true,
                RepairedJson = repaired,
                RepairHint = hints.Count > 0 ? string.Join("; ", hints) : null
            };

        repaired = RepairTruncatedJson(repaired, hints);

        if (TryParseJson(repaired, out _))
            return new ToolCallRepairResult
            {
                Success = true,
                RepairedJson = repaired,
                RepairHint = hints.Count > 0 ? string.Join("; ", hints) : null
            };

        return new ToolCallRepairResult
        {
            Success = false,
            RepairedJson = repaired,
            RepairHint = $"JSON repair failed. Original: {TruncateForHint(rawJson)}"
        };
    }

    public static ArgumentRepairResult RepairArguments(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        ToolSchema? schema)
    {
        if (arguments is null || arguments.Count == 0)
            return new ArgumentRepairResult { RepairedArguments = arguments ?? new Dictionary<string, JsonElement>() };

        if (schema?.Properties is null || schema.Properties.Count == 0)
            return new ArgumentRepairResult { RepairedArguments = arguments };

        var repaired = arguments;
        var hints = new List<string>();
        var modified = false;

        var nameRepairs = RepairParameterNames(arguments, schema);
        if (nameRepairs.Modified)
        {
            repaired = nameRepairs.Arguments;
            hints.Add(nameRepairs.Hint!);
            modified = true;
        }

        var typeRepairs = RepairArgumentTypes(repaired, schema);
        if (typeRepairs.Modified)
        {
            repaired = typeRepairs.Arguments;
            hints.Add(typeRepairs.Hint!);
            modified = true;
        }

        return new ArgumentRepairResult
        {
            RepairedArguments = modified ? repaired : arguments,
            RepairHint = hints.Count > 0 ? string.Join("; ", hints) : null
        };
    }

    /// <summary>
    /// 工具名归一化 — 将 LLM 返回的任意大小写工具名（如 read/READ/Read）归一化为标准名
    /// 利用各工具名枚举的 FromValue（OrdinalIgnoreCase）反查，找到标准名后返回
    /// 找不到匹配则返回原名（可能是 MCP 工具或自定义工具）
    /// </summary>
    public static string RepairToolName(string? toolName)
    {
        if (string.IsNullOrEmpty(toolName))
            return toolName ?? string.Empty;

        foreach (var resolver in ToolNameResolvers)
        {
            var standard = resolver(toolName);
            if (standard is not null)
                return standard;
        }

        return toolName;
    }

    private static readonly Func<string, string?>[] ToolNameResolvers =
    [
        name => FileToolNameExtensions.FromValue(name)?.ToValue(),
        name => SearchToolNameExtensions.FromValue(name)?.ToValue(),
        name => WebToolNameExtensions.FromValue(name)?.ToValue(),
        name => ShellToolNameExtensions.FromValue(name)?.ToValue(),
        name => TaskToolNameExtensions.FromValue(name)?.ToValue(),
        name => TodoToolNameExtensions.FromValue(name)?.ToValue(),
        name => CodeToolNameExtensions.FromValue(name)?.ToValue(),
        name => GitToolNameExtensions.FromValue(name)?.ToValue(),
        name => NotebookToolNameExtensions.FromValue(name)?.ToValue(),
        name => MemoryToolNameExtensions.FromValue(name)?.ToValue(),
        name => PlanToolNameExtensions.FromValue(name)?.ToValue(),
        name => SkillToolNameExtensions.FromValue(name)?.ToValue(),
        name => McpToolNameExtensions.FromValue(name)?.ToValue(),
        name => CronToolNameExtensions.FromValue(name)?.ToValue(),
        name => SystemToolNameExtensions.FromValue(name)?.ToValue(),
        name => InteractionToolNameExtensions.FromValue(name)?.ToValue(),
        name => AgentToolNameExtensions.FromValue(name)?.ToValue(),
        name => TeamToolNameExtensions.FromValue(name)?.ToValue(),
        name => WorkflowToolNameExtensions.FromValue(name)?.ToValue(),
        name => WorktreeToolNameExtensions.FromValue(name)?.ToValue(),
    ];

    private static bool TryParseJson(string json, out JsonDocument? doc)
    {
        try
        {
            doc = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            doc = null;
            return false;
        }
    }

    private static string RemoveTrailingCommas(string json, List<string> hints)
    {
        bool changed = false;
        var result = new StringBuilder(json.Length);
        int i = 0;

        while (i < json.Length)
        {
            if (json[i] == '"')
            {
                int start = i;
                i++;
                while (i < json.Length)
                {
                    if (json[i] == '\\' && i + 1 < json.Length) { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            if (json[i] == ',')
            {
                int j = i + 1;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;

                if (j < json.Length && (json[j] == '}' || json[j] == ']'))
                {
                    changed = true;
                    i++;
                    continue;
                }
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("removed trailing comma(s)");

        return result.ToString();
    }

    private static string FixUnquotedKeys(string json, List<string> hints)
    {
        bool changed = false;
        var result = new StringBuilder(json.Length);
        int i = 0;

        while (i < json.Length)
        {
            if (json[i] == '"')
            {
                int start = i;
                i++;
                while (i < json.Length)
                {
                    if (json[i] == '\\' && i + 1 < json.Length) { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            if (json[i] == '{' || json[i] == ',')
            {
                result.Append(json[i]);
                i++;
                while (i < json.Length && char.IsWhiteSpace(json[i])) { result.Append(json[i]); i++; }

                if (i < json.Length && json[i] == '"')
                {
                    continue;
                }

                if (i < json.Length && (char.IsLetter(json[i]) || json[i] == '_'))
                {
                    int keyStart = i;
                    while (i < json.Length && (char.IsLetterOrDigit(json[i]) || json[i] == '_')) i++;

                    int j = i;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;

                    if (j < json.Length && json[j] == ':')
                    {
                        result.Append('"');
                        result.Append(json.AsSpan(keyStart, i - keyStart));
                        result.Append('"');
                        changed = true;
                        continue;
                    }
                }

                continue;
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("added quotes to unquoted key(s)");

        return result.ToString();
    }

    private static string FixSingleQuotedKeys(string json, List<string> hints)
    {
        bool changed = false;
        var result = new StringBuilder(json.Length);
        int i = 0;

        while (i < json.Length)
        {
            if (json[i] == '"')
            {
                int start = i;
                i++;
                while (i < json.Length)
                {
                    if (json[i] == '\\' && i + 1 < json.Length) { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            if (json[i] == '\'')
            {
                int contentStart = i + 1;
                int contentEnd = contentStart;
                while (contentEnd < json.Length && json[contentEnd] != '\'') contentEnd++;

                if (contentEnd < json.Length)
                {
                    result.Append('"');
                    result.Append(json.AsSpan(contentStart, contentEnd - contentStart));
                    result.Append('"');
                    changed = true;
                    i = contentEnd + 1;
                    continue;
                }
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("converted single-quoted string(s) to double quotes");

        return result.ToString();
    }

    private static (Dictionary<string, JsonElement> Arguments, bool Modified, string? Hint) RepairParameterNames(
        Dictionary<string, JsonElement> arguments,
        ToolSchema schema)
    {
        var schemaProps = schema.Properties.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repairs = new List<string>();
        var repaired = new Dictionary<string, JsonElement>(arguments.Count);

        foreach (var (key, value) in arguments)
        {
            if (schemaProps.Contains(key))
            {
                // 使用 OrdinalIgnoreCase HashSet 时，Contains("Pattern") 对 "pattern" 返回 true
                // 但必须用 schema 中的实际 key（"pattern"）存储，否则下游工具按精确匹配找不到参数
                var actualKey = FindActualKey(key, schemaProps) ?? key;
                repaired[actualKey] = value;
                if (!string.Equals(actualKey, key, StringComparison.Ordinal))
                {
                    repairs.Add($"'{key}' → '{actualKey}'");
                }
                continue;
            }

            var matched = TryMatchParameter(key, schemaProps);
            if (matched is not null)
            {
                // 不覆盖已由直接匹配设置的值（直接匹配优先于别名匹配）
                // 场景: schema 有 file_path，LLM 同时发送 file_path(直接匹配) 和 path(别名→filePath→snake_case file_path)
                // 若别名覆盖直接匹配，会导致正确的 file_path 值被丢弃
                if (!repaired.ContainsKey(matched))
                {
                    repaired[matched] = value;
                    repairs.Add($"'{key}' → '{matched}'");
                }
                else
                {
                    // 目标 key 已被直接匹配占用，保留原 key 避免数据丢失
                    repaired[key] = value;
                }
            }
            else
            {
                repaired[key] = value;
            }
        }

        if (repairs.Count == 0)
            return (arguments, false, null);

        return (repaired, true, $"renamed parameter(s): {string.Join(", ", repairs)}");
    }

    private static string? TryMatchParameter(string wrongName, HashSet<string> schemaProps)
    {
        if (ParameterAliases.TryGetValue(wrongName, out var alias))
        {
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

        foreach (var schemaKey in schemaProps)
        {
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

    private static string? FindActualKey(string key, HashSet<string> schemaProps)
    {
        foreach (var schemaKey in schemaProps)
        {
            if (string.Equals(schemaKey, key, StringComparison.OrdinalIgnoreCase))
                return schemaKey;
        }
        return key;
    }

    private static (Dictionary<string, JsonElement> Arguments, bool Modified, string? Hint) RepairArgumentTypes(
        Dictionary<string, JsonElement> arguments,
        ToolSchema schema)
    {
        var repairs = new List<string>();
        var repaired = new Dictionary<string, JsonElement>(arguments.Count);
        bool modified = false;

        foreach (var (key, value) in arguments)
        {
            if (!schema.Properties.TryGetValue(key, out var propSchema))
            {
                repaired[key] = value;
                continue;
            }

            var expectedType = propSchema.Type?.ToLowerInvariant();
            if (string.IsNullOrEmpty(expectedType))
            {
                repaired[key] = value;
                continue;
            }

            var (converted, wasConverted) = TryConvertType(value, expectedType);
            if (wasConverted)
            {
                repaired[key] = converted;
                repairs.Add($"'{key}' type corrected to {expectedType}");
                modified = true;
            }
            else
            {
                repaired[key] = value;
            }
        }

        if (!modified)
            return (arguments, false, null);

        return (repaired, true, string.Join("; ", repairs));
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertType(JsonElement value, string expectedType)
    {
        return expectedType switch
        {
            "string" => TryConvertToString(value),
            "integer" => TryConvertToInteger(value),
            "number" => TryConvertToNumber(value),
            "boolean" => TryConvertToBoolean(value),
            "array" => TryConvertToArray(value),
            _ => (value, false)
        };
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertToString(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                var numStr = value.TryGetInt64(out var longVal) ? longVal.ToString() : value.GetDouble().ToString(CultureInfo.InvariantCulture);
                return (JsonElementHelper.FromString(numStr), true);

            case JsonValueKind.True:
            case JsonValueKind.False:
                return (JsonElementHelper.FromString(value.GetBoolean().ToString().ToLowerInvariant()), true);

            case JsonValueKind.Array:
                if (value.GetArrayLength() == 0)
                    return (JsonElementHelper.FromString(""), true);
                if (value.GetArrayLength() == 1)
                    return (value[0].ValueKind == JsonValueKind.String ? value[0].Clone() : JsonElementHelper.FromString(value[0].GetRawText()), true);
                return (value[0].ValueKind == JsonValueKind.String ? value[0].Clone() : JsonElementHelper.FromString(value[0].GetRawText()), true);

            case JsonValueKind.Object:
                return (JsonElementHelper.FromString(value.GetRawText()), true);

            default:
                return (value, false);
        }
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertToInteger(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString()!;
            if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
                return (JsonElementHelper.FromInt32(intVal), true);
            if (long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
                return (JsonElementHelper.FromInt64(longVal), true);
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var intVal))
                return (JsonElementHelper.FromInt32(intVal), false);
        }

        return (value, false);
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertToNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString()!;
            if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal))
                return (JsonElementHelper.FromDouble(doubleVal), true);
        }

        return (value, false);
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertToBoolean(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString()!;
            if (bool.TryParse(str, out var boolVal))
                return (JsonElementHelper.FromBoolean(boolVal), true);
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var intVal))
                return (JsonElementHelper.FromBoolean(intVal != 0), true);
        }

        return (value, false);
    }

    private static (JsonElement Converted, bool WasConverted) TryConvertToArray(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString()!;
            if (str.StartsWith('['))
            {
                try
                {
                    var arr = JsonDocument.Parse(str);
                    if (arr.RootElement.ValueKind == JsonValueKind.Array)
                        return (arr.RootElement.Clone(), true);
                }
                catch (JsonException)
                {
                    System.Diagnostics.Debug.WriteLine($"ToolCallRepairService: failed to parse string as JSON array");
                }
            }
        }

        return (value, false);
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var parts = name.Split('_');
        if (parts.Length <= 1) return name;
        var sb = new StringBuilder(name.Length);
        sb.Append(parts[0].ToLowerInvariant());
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                sb.Append(char.ToUpperInvariant(parts[i][0]));
                sb.Append(parts[i].Substring(1).ToLowerInvariant());
            }
        }
        return sb.ToString();
    }

    private static string TruncateForHint(string text, int maxLength = 200)
    {
        if (text.Length <= maxLength) return text;
        return $"{text[..(maxLength / 2)]}...{text[^(maxLength / 2)..]}";
    }

    private static string RepairTruncatedJson(string json, List<string> hints)
    {
        var sb = new StringBuilder(json.Length + 16);
        var stack = new Stack<char>();
        var inString = false;
        var escape = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];

            if (escape)
            {
                sb.Append(c);
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                sb.Append(c);
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            if (inString)
            {
                sb.Append(c);
                continue;
            }

            if (c is '{' or '[')
            {
                stack.Push(c);
                sb.Append(c);
                continue;
            }

            if (c is '}' or ']')
            {
                if (stack.Count > 0)
                    stack.Pop();
                sb.Append(c);
                continue;
            }

            sb.Append(c);
        }

        var modified = inString || stack.Count > 0 || sb.Length != json.Length;

        if (inString)
        {
            sb.Append('"');
            hints.Add("closed truncated string");
        }

        while (stack.Count > 0)
        {
            var opener = stack.Pop();
            var closer = opener == '{' ? '}' : ']';

            var len = sb.Length;
            while (len > 0 && char.IsWhiteSpace(sb[len - 1])) len--;
            if (len > 0 && sb[len - 1] == ',')
                sb.Length = len - 1;

            sb.Append(closer);
            modified = true;
        }

        if (!modified)
            return json;

        if (sb.Length > json.Length)
            hints.Add("closed truncated JSON structure");

        return sb.ToString();
    }
}
