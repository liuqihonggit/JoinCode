namespace NativeTui.Core.Repl;

/// <summary>
/// 工具主参数映射（消息操作域）— 对齐基准 messageActions.tsx PRIMARY_INPUT（12 工具）：
/// Bash→command、Read/Write/Edit/NotebookEdit→path、Grep/Glob→pattern、WebFetch→url、
/// WebSearch→query、Task/Agent→prompt、Tmux→command（args 数组拼接 "tmux a b"）。
/// Args 容错解析（Args 为字符串）：JSON object（JsonDocument 动态字段提取 — AOT 安全）优先，
/// 解析失败/非 object 形态回退首个双引号内字符串（简写摘要形态兜底）。
/// 与基准的有意偏离：Agent 的 prompt 优先、description 兜底（本项目引擎 Agent 调用
/// 以 description 为组头展示面的参数形态适配；基准仅查 prompt）。
/// </summary>
public static class PrimaryInputMap
{
    /// <summary>12 工具主参数表（工具名精确匹配 → 标签 + 字段名集合；ArrayField = 数组拼接特例 Tmux）。</summary>
    private static readonly FrozenDictionary<string, ToolPrimaryInput> Map = new Dictionary<string, ToolPrimaryInput>
    {
        ["Read"] = new("path", ["file_path"]),
        ["Edit"] = new("path", ["file_path"]),
        ["Write"] = new("path", ["file_path"]),
        ["NotebookEdit"] = new("path", ["notebook_path"]),
        ["Bash"] = new("command", ["command"]),
        ["Grep"] = new("pattern", ["pattern"]),
        ["Glob"] = new("pattern", ["pattern"]),
        ["WebFetch"] = new("url", ["url"]),
        ["WebSearch"] = new("query", ["query"]),
        ["Task"] = new("prompt", ["prompt"]),
        ["Agent"] = new("prompt", ["prompt", "description"]),
        ["Tmux"] = new("command", [], ArrayField: "args"),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>工具是否在主参数映射内（操作条 p 复制段显示依据 — 基准 applies: toolName in PRIMARY_INPUT）。</summary>
    public static bool Supports(string toolName) => Map.ContainsKey(toolName);

    /// <summary>主参数标签（path/command/pattern/…；未映射工具 null — 操作条 "复制 {label}" 文案源）。</summary>
    public static string? LabelOf(string toolName) =>
        Map.TryGetValue(toolName, out var input) ? input.Label : null;

    /// <summary>提取工具调用的主参数值（JSON 容错解析 → 字段提取 → 首引号回退；未映射工具/全 miss → null）。</summary>
    public static string? ExtractPrimaryInput(string toolName, string args) =>
        Map.TryGetValue(toolName, out var input) ? ExtractFromArgs(input, args) : null;

    /// <summary>从参数串提取主参数值：合法 JSON object → 字段提取结果（miss 即 null 不回退 — object 内
    /// 首引号是 key，回退会误提取）；解析失败/非 object（简写摘要形态）→ 首引号回退。</summary>
    private static string? ExtractFromArgs(ToolPrimaryInput input, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return null;
        }

        return TryExtractFromJson(input, args, out var fromJson) ? fromJson : FirstQuoted(args);
    }

    /// <summary>JSON object 解析 + 字段提取（JsonDocument — 动态字段提取 AOT 安全；解析失败/非 object → false 走回退）。</summary>
    private static bool TryExtractFromJson(ToolPrimaryInput input, string args, out string? value)
    {
        try
        {
            using var document = JsonDocument.Parse(args);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                value = null;
                return false;
            }

            var root = document.RootElement;
            foreach (var field in input.StringFields)
            {
                if (root.TryGetProperty(field, out var property)
                    && property.ValueKind == JsonValueKind.String
                    && property.GetString() is { Length: > 0 } text)
                {
                    value = text;
                    return true;
                }
            }

            value = input.ArrayField is { } arrayField
                && root.TryGetProperty(arrayField, out var array)
                && array.ValueKind == JsonValueKind.Array
                    ? JoinTmuxArgs(array)
                    : null;
            return true;
        }
        catch (JsonException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>Tmux args 数组拼接（"tmux arg1 arg2" — 基准字面 `tmux ${args.join(' ')}`；空数组/非字符串元素全滤）。</summary>
    private static string? JoinTmuxArgs(JsonElement array)
    {
        var parts = array.EnumerateArray()
            .Where(static element => element.ValueKind == JsonValueKind.String)
            .Select(static element => element.GetString())
            .Where(static text => text is { Length: > 0 })
            .ToList();
        return parts.Count == 0 ? null : $"tmux {string.Join(' ', parts)}";
    }

    /// <summary>首引号回退 — 提取参数串中首个双引号内的字符串（Span 扫描；无引号/未闭合 → null）。</summary>
    private static string? FirstQuoted(string args)
    {
        var span = args.AsSpan();
        var open = span.IndexOf('"');
        if (open < 0)
        {
            return null;
        }

        var close = span[(open + 1)..].IndexOf('"');
        return close < 0 ? null : span.Slice(open + 1, close).ToString();
    }

    /// <summary>工具主参数描述（映射值形态）— Label 为操作条文案（"复制 {label}"）；StringFields 依序首个命中；
    /// ArrayField 为数组拼接特例（Tmux args）。</summary>
    /// <param name="Label">主参数标签（path/command/pattern/url/query/prompt）。</param>
    /// <param name="StringFields">字符串字段名集合（依序查找首个命中）。</param>
    /// <param name="ArrayField">数组字段名（null = 无数组特例）。</param>
    private sealed record ToolPrimaryInput(string Label, string[] StringFields, string? ArrayField = null);
}
