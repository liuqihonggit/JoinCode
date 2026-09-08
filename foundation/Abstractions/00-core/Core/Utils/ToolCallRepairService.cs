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

internal static class ToolCallRepairService
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

        var json = StripBom(rawJson.Trim());
        json = StripTrailingSemicolon(json);
        json = StripOuterQuotes(json);

        if (TryParseJson(json, out _))
            return new ToolCallRepairResult { Success = true, RepairedJson = json };

        var hints = new List<string>();
        var repaired = json;

        repaired = FixRawNewlines(repaired, hints);
        repaired = RemoveTrailingCommas(repaired, hints);
        repaired = FixUnquotedKeys(repaired, hints);
        repaired = FixUnquotedValues(repaired, hints);
        repaired = FixSingleQuotedStrings(repaired, hints);
        repaired = FixEscapeSequences(repaired, hints);
        repaired = FixHexAndLeadingZeroNumbers(repaired, hints);
        repaired = FixNamedFloatingPointLiterals(repaired, hints);

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
            hints.Add(nameRepairs.Hint ?? "Parameter names repaired");
            modified = true;
        }

        var typeRepairs = RepairArgumentTypes(repaired, schema);
        if (typeRepairs.Modified)
        {
            repaired = typeRepairs.Arguments;
            hints.Add(typeRepairs.Hint ?? "Argument types repaired");
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

        // Fallback: 去下划线模糊匹配(WEBFETCH → web_fetch, DIRECTORYLIST → directory_list)
        return UnderscoreFallback(toolName) ?? toolName;
    }

    /// <summary>
    /// 去下划线模糊匹配 — 当 FromValue 精确匹配失败时,去掉下划线后 OrdinalIgnoreCase 比较
    /// <para>场景: WEBFETCH → web_fetch, webfetch → web_fetch</para>
    /// </summary>
    private static string? UnderscoreFallback(string name)
    {
        var normalized = name.Replace("_", "");
        return UnderscoreFallbackCore<FileToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<SearchToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<WebToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<ShellToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<TaskToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<TodoToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<CodeToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<GitToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<NotebookToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<MemoryToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<PlanToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<SkillToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<McpToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<CronToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<SystemToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<InteractionToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<AgentToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<TeamToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<WorkflowToolName>(normalized, v => v.ToValue())
            ?? UnderscoreFallbackCore<WorktreeToolName>(normalized, v => v.ToValue());
    }

    private static string? UnderscoreFallbackCore<TEnum>(string normalized, Func<TEnum, string> toValue) where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var enumValue = toValue(value);
            if (enumValue.Replace("_", "").Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return enumValue;
        }
        return null;
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

    /// <summary>
    /// 工具名模糊匹配 — 当用户/AI 调用不存在的工具名时,推荐相似工具名
    /// <para>匹配策略: 精确(大小写不同) > 前缀 > 子串 > 编辑距离≤3</para>
    /// <para>返回按相似度降序排列的工具名,最多 5 个</para>
    /// </summary>
    public static IReadOnlyList<string> SuggestToolNames(string input, IEnumerable<string> availableTools)
    {
        if (string.IsNullOrEmpty(input) || availableTools is null)
            return Array.Empty<string>();

        var scored = new List<(string Name, int Score)>();
        foreach (var tool in availableTools)
        {
            var score = ComputeNameSimilarity(input, tool);
            if (score > 0)
                scored.Add((tool, score));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Take(5)
            .Select(s => s.Name)
            .ToList();
    }

    private static int ComputeNameSimilarity(string input, string candidate)
    {
        if (string.Equals(input, candidate, StringComparison.OrdinalIgnoreCase))
            return 100;

        if (input.Length > candidate.Length && input.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
            return 80 - (input.Length - candidate.Length);

        if (input.Length < candidate.Length && candidate.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            return 60 - (candidate.Length - input.Length);

        if (input.Contains(candidate, StringComparison.OrdinalIgnoreCase) || candidate.Contains(input, StringComparison.OrdinalIgnoreCase))
            return 40;

        var dist = LevenshteinIgnoreCase(input, candidate);
        if (dist <= 3)
            return 30 - dist;

        return 0;
    }

    /// <summary>Levenshtein 编辑距离(大小写不敏感)</summary>
    private static int LevenshteinIgnoreCase(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    /// <summary>
    /// 生成跨 shell 调用示例文本 — 帮助 AI/用户正确传递 JSON 参数
    /// <para>覆盖 PowerShell(--%)、Bash(单引号)、Cmd(转义引号)三种 shell</para>
    /// </summary>
    internal static string BuildShellCallExamples(string toolName)
    {
        return $$"""
调用示例:
  PowerShell: jcc mcp_call {{toolName}} --% "{\"key\":\"value\"}"
  Bash:       jcc mcp_call {{toolName}} '{"key":"value"}'
  Cmd:        jcc mcp_call {{toolName}} "{\"key\":\"value\"}"
""";
    }

    /// <summary>
    /// 检测"引号被 shell 剥落"特征并返回修正写法提示 — 以 { 开头、有冒号、但无双引号
    /// <para>返回 null 表示未检测到该特征(不提示)</para>
    /// </summary>
    internal static string? BuildShellQuoteHint(string json)
    {
        if (json.Length > 0 && json[0] == '{' && json.Contains(':') && !json.Contains('"'))
        {
            return """
提示: 输入看起来像被 shell 剥掉了引号。
  PowerShell: 用 --% 停止解析,或用 \" 转义双引号
  示例: jcc mcp_call <tool> --% "{\"key\":\"value\"}"
""";
        }
        return null;
    }

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

    /// <summary>
    /// 剥离 UTF-8/UTF-16 BOM 头
    /// </summary>
    private static string StripBom(string input)
    {
        var span = input.AsSpan();
        while (span.Length > 0 && (span[0] == '\uFEFF' || span[0] == '\uFFFE' || span[0] == '\u0000'))
            span = span[1..];

        return span.ToString();
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

    /// <summary>
    /// 给无引号的 JSON string value 加引号 — :value, → :"value",  :value} → :"value"}
    /// <para>跳过数字、true/false/null、嵌套对象{}和数组[]、已加引号的字符串</para>
    /// <para>字符级遍历，正确跳过字符串内的冒号，不会误处理字符串内的 :value, 模式</para>
    /// </summary>
    private static string FixUnquotedValues(string json, List<string> hints)
    {
        bool changed = false;
        var result = new StringBuilder(json.Length);
        int i = 0;

        // 将 json[start..end] 加双引号后追加到 result,裸反斜杠转义为 \\ (JSON 合法)
        static void AppendQuotedValue(StringBuilder sb, string s, int start, int end)
        {
            sb.Append('"');
            var span = s.AsSpan(start, end - start);
            for (int k = 0; k < span.Length; k++)
            {
                if (span[k] == '\\')
                    sb.Append("\\\\");
                else
                    sb.Append(span[k]);
            }
            sb.Append('"');
        }

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

            if (json[i] == ':')
            {
                result.Append(json[i]);
                i++;

                while (i < json.Length && char.IsWhiteSpace(json[i])) { result.Append(json[i]); i++; }
                if (i >= json.Length) continue;

                var c = json[i];
                if (c == '"' || c == '\'' || c == '{' || c == '[') continue;
                if (char.IsDigit(c) || c == '-' || c == '+') continue;
                if (IsLiteralAt(json, i, "true") || IsLiteralAt(json, i, "false") || IsLiteralAt(json, i, "null"))
                    continue;

                int valueStart = i;
                // 保守收集: 到空格/逗号/}/] 停(值不含空格的快速路径)
                while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']' && !char.IsWhiteSpace(json[i]))
                    i++;

                if (i > valueStart)
                {
                    int j = i;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                    if (j < json.Length && (json[j] == ',' || json[j] == '}' || json[j] == ']'))
                    {
                        AppendQuotedValue(result, json, valueStart, i);
                        changed = true;
                        continue;
                    }
                }

                // 保守收集失败(值含空格,如 PowerShell 剥引号后的 {prompt:echo hello})
                // → 激进收集: 允许空格,到 ,/}/]/{/[ 停,整体加引号
                i = valueStart;
                while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']' && json[i] != '{' && json[i] != '[')
                    i++;

                // 去掉尾部空白(避免 "echo hello " 带尾部空格在引号内)
                int valueEnd = i;
                while (valueEnd > valueStart && char.IsWhiteSpace(json[valueEnd - 1])) valueEnd--;

                if (valueEnd > valueStart)
                {
                    AppendQuotedValue(result, json, valueStart, valueEnd);
                    // 尾部空白在引号外原样输出
                    for (int k = valueEnd; k < i; k++)
                        result.Append(json[k]);
                    changed = true;
                    continue;
                }

                // 激进收集也失败(值为空或首字符即分隔符),原样输出
                result.Append(json.AsSpan(valueStart, i - valueStart));
                continue;
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("added quotes to unquoted value(s)");

        return result.ToString();
    }

    /// <summary>
    /// 检查字符串指定位置是否匹配某个字面量（true/false/null），且后面是单词边界
    /// </summary>
    private static bool IsLiteralAt(string s, int index, string literal)
    {
        if (index + literal.Length > s.Length) return false;
        for (int k = 0; k < literal.Length; k++)
        {
            if (char.ToLowerInvariant(s[index + k]) != literal[k]) return false;
        }
        if (index + literal.Length < s.Length)
        {
            var next = s[index + literal.Length];
            if (char.IsLetterOrDigit(next) || next == '_') return false;
        }
        return true;
    }

    private static string StripTrailingSemicolon(string json)
    {
        var trimmed = json.AsSpan().Trim();
        if (trimmed.Length > 0 && trimmed[trimmed.Length - 1] == ';')
        {
            var end = json.Length;
            while (end > 0 && char.IsWhiteSpace(json[end - 1])) end--;
            if (end > 0 && json[end - 1] == ';')
                return json[..(end - 1)];
        }

        return json;
    }

    /// <summary>
    /// 去除 JSON 外层多余引号（单引号或双引号）— Shell 转义常见问题
    /// <para>如 '"{"key":"value"}"' → '{"key":"value"}'</para>
    /// <para>幂等：如果去除后不是合法 JSON 开头，保留原始引号</para>
    /// </summary>
    private static string StripOuterQuotes(string json)
    {
        if (json.Length < 2)
            return json;

        var first = json[0];
        var last = json[^1];
        if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
        {
            var inner = json[1..^1];
            if (inner.Length > 0 && (inner[0] == '{' || inner[0] == '['))
                return inner;
        }

        return json;
    }

    /// <summary>
    /// 修复裸换行符和制表符 — Shell 转义常见问题
    /// <para>PowerShell 双引号字符串中 \n 被解释为实际换行符 → 替换回 \n 转义序列</para>
    /// <para>实际换行符在 JSON 字符串值中是非法的，必须替换为 \n 转义序列</para>
    /// <para>幂等：已转义的 \\n 不受影响（它是两个字符 \ 和 n，不是裸换行符）</para>
    /// </summary>
    private static string FixRawNewlines(string json, List<string> hints)
    {
        if (!json.Contains('\r') && !json.Contains('\n') && !json.Contains('\t'))
            return json;

        var repaired = json.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n").Replace("\t", "\\t");
        hints.Add("replaced raw newline/tab with escape sequences");
        return repaired;
    }

    private static string FixSingleQuotedStrings(string json, List<string> hints)
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

    /// <summary>
    /// 修复十六进制数字（0xFF → 255）与前导零数字（0123 → 123）。
    /// 字符串内容不受影响；对已是合法 JSON 的输入不产生任何改动。
    /// </summary>
    /// <summary>
    /// 修复 JSON 字符串中的非法转义序列和裸控制字符。
    /// 1. \' → ' （标准 JSON 字符串内不需要转义单引号，System.Text.Json 会拒绝）
    /// 2. 裸控制字符（0x00-0x1F）→ 对应 \n \t \r 等转义序列
    /// 3. 无效反斜杠转义（如 \p \w \R）→ 双写反斜杠（\\p \\w \\R），处理 Windows 路径
    /// </summary>
    private static string FixEscapeSequences(string json, List<string> hints)
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
                    if (json[i] == '\\' && i + 1 < json.Length)
                    {
                        var next = json[i + 1];
                        if (next == '\'')
                        {
                            result.Append(json.AsSpan(start, i - start));
                            result.Append('\'');
                            changed = true;
                            i += 2;
                            start = i;
                            continue;
                        }

                        if (next is not ('"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u'))
                        {
                            result.Append(json.AsSpan(start, i - start));
                            result.Append("\\\\");
                            changed = true;
                            i++;
                            start = i;
                            continue;
                        }

                        i += 2;
                        continue;
                    }

                    if (json[i] == '"') { i++; break; }

                    if (json[i] < 0x20)
                    {
                        result.Append(json.AsSpan(start, i - start));
                        result.Append(json[i] switch
                        {
                            '\n' => "\\n",
                            '\r' => "\\r",
                            '\t' => "\\t",
                            '\b' => "\\b",
                            '\f' => "\\f",
                            _ => $"\\u{((int)json[i]):x4}"
                        });
                        changed = true;
                        i++;
                        start = i;
                        continue;
                    }

                    i++;
                }

                result.Append(json.AsSpan(start, i - start));
                continue;
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("fixed escape sequence(s)/control character(s)");

        return result.ToString();
    }

    /// <summary>
    /// 修复 JSON 中的 Infinity / -Infinity / NaN 字面量。
    /// 标准 JSON 不支持这些值，System.Text.Json 默认拒绝。
    /// 策略：将裸字面量转为字符串（如 Infinity → "Infinity"），
    /// 由 JsonLenientCoercer 在类型转换层进一步处理。
    /// </summary>
    private static string FixNamedFloatingPointLiterals(string json, List<string> hints)
    {
        bool changed = false;
        var literals = new List<string>();
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

            if (i + 7 < json.Length && json.AsSpan(i, 8) is "Infinity")
            {
                var before = i > 0 ? json[i - 1] : '\0';
                var afterIdx = i + 8;
                var after = afterIdx < json.Length ? json[afterIdx] : '\0';
                if (!IsAlphaNumeric(before) && !IsAlphaNumeric(after))
                {
                    result.Append("\"Infinity\"");
                    changed = true;
                    literals.Add("Infinity");
                    i += 8;
                    continue;
                }
            }

            if (i + 8 < json.Length && json.AsSpan(i, 9) is "-Infinity")
            {
                var before = i > 0 ? json[i - 1] : '\0';
                var afterIdx = i + 9;
                var after = afterIdx < json.Length ? json[afterIdx] : '\0';
                if (!IsAlphaNumeric(before) && !IsAlphaNumeric(after))
                {
                    result.Append("\"-Infinity\"");
                    changed = true;
                    literals.Add("-Infinity");
                    i += 9;
                    continue;
                }
            }

            if (i + 2 < json.Length && json.AsSpan(i, 3) is "NaN")
            {
                var before = i > 0 ? json[i - 1] : '\0';
                var afterIdx = i + 3;
                var after = afterIdx < json.Length ? json[afterIdx] : '\0';
                if (!IsAlphaNumeric(before) && !IsAlphaNumeric(after))
                {
                    result.Append("\"NaN\"");
                    changed = true;
                    literals.Add("NaN");
                    i += 3;
                    continue;
                }
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add($"quoted named floating-point literal(s): {string.Join(", ", literals)}");

        return result.ToString();
    }

    private static bool IsAlphaNumeric(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';

    private static string FixHexAndLeadingZeroNumbers(string json, List<string> hints)
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

            // 十六进制：0x / 0X 后跟十六进制数字
            if (json[i] == '0' && i + 1 < json.Length && (json[i + 1] == 'x' || json[i + 1] == 'X'))
            {
                int j = i + 2;
                int hexStart = j;
                while (j < json.Length && IsHexDigit(json[j])) j++;

                if (j > hexStart
                    && ulong.TryParse(json.AsSpan(hexStart, j - hexStart), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var hexVal))
                {
                    result.Append(hexVal.ToString(CultureInfo.InvariantCulture));
                    changed = true;
                    i = j;
                    continue;
                }
            }

            // 前导零整数：0 紧跟数字（如 0123）→ 去前导零（保留至少一位）
            if (json[i] == '0' && i + 1 < json.Length && json[i + 1] is >= '0' and <= '9')
            {
                int j = i + 1;
                while (j < json.Length && json[j] is >= '0' and <= '9') j++;

                int k = i;
                while (k < j - 1 && json[k] == '0') k++;

                result.Append(json.AsSpan(k, j - k));
                changed = true;
                i = j;
                continue;
            }

            result.Append(json[i]);
            i++;
        }

        if (changed)
            hints.Add("converted hex/leading-zero number(s)");

        return result.ToString();
    }

    private static bool IsHexDigit(char c)
    {
        return c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
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
            else if (str.StartsWith('{'))
            {
                try
                {
                    var obj = JsonDocument.Parse(str);
                    if (obj.RootElement.ValueKind == JsonValueKind.Object)
                        return (JsonDocument.Parse($"[{str}]").RootElement.Clone(), true);
                }
                catch (JsonException)
                {
                    System.Diagnostics.Debug.WriteLine($"ToolCallRepairService: failed to parse string as JSON object for array wrap");
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
