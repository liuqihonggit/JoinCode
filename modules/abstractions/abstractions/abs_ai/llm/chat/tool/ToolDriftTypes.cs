namespace JoinCode.Abstractions.LLM.Chat;

public enum ToolDriftKind {
    [EnumValue("identity")]
    Identity,
    [EnumValue("append")]
    Append,
    [EnumValue("edit")]
    Edit,
    [EnumValue("reorder")]
    Reorder,
    [EnumValue("remove")]
    Remove
}

public sealed class ToolDriftReport {
    /// <summary>获取漂移类型。</summary>
    public ToolDriftKind Kind { get; init; }
    /// <summary>获取新增工具名称列表。</summary>
    public IReadOnlyList<string> AddedNames { get; init; } = [];
    /// <summary>获取移除工具名称列表。</summary>
    public IReadOnlyList<string> RemovedNames { get; init; } = [];
    /// <summary>获取编辑工具名称列表。</summary>
    public IReadOnlyList<string> EditedNames { get; init; } = [];
    /// <summary>获取重排序工具名称列表。</summary>
    public IReadOnlyList<string> ReorderedNames { get; init; } = [];
    /// <summary>获取漂移摘要。</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>获取是否缓存安全。</summary>
    public bool IsCacheSafe => Kind is ToolDriftKind.Identity or ToolDriftKind.Append;

    /// <summary>
    /// 返回工具名脱敏后的副本 — MCP 工具名（mcp__ 前缀，用户配置，可能泄露路径）折叠为 mcp，
    /// 内置工具名是固定词表无需脱敏。对齐 TS sanitizeToolName。
    /// </summary>
    public ToolDriftReport WithSanitizedNames() {
        return new ToolDriftReport {
            Kind = Kind,
            AddedNames = SanitizeNames(AddedNames),
            RemovedNames = SanitizeNames(RemovedNames),
            EditedNames = SanitizeNames(EditedNames),
            ReorderedNames = SanitizeNames(ReorderedNames),
            Summary = SanitizeSummary(Summary)
        };
    }

    private static IReadOnlyList<string> SanitizeNames(IReadOnlyList<string> names) {
        if (names.Count == 0) return names;
        var result = new List<string>(names.Count);
        for (var i = 0; i < names.Count; i++) {
            result.Add(SanitizeToolName(names[i]));
        }
        return result;
    }

    private static string SanitizeSummary(string summary) {
        if (string.IsNullOrEmpty(summary)) return summary;
        var span = summary.AsSpan();
        var result = new StringBuilder(summary.Length);
        var i = 0;
        while (i < span.Length) {
            var spaceIdx = span.Slice(i).IndexOf(' ');
            if (spaceIdx < 0) {
                result.Append(SanitizeToolName(span.Slice(i).ToString()));
                break;
            }

            result.Append(SanitizeToolName(span.Slice(i, spaceIdx).ToString()));
            result.Append(' ');
            i += spaceIdx + 1;
        }
        return result.ToString();
    }

    private static string SanitizeToolName(string name)
        => name.StartsWith("mcp__", StringComparison.Ordinal) ? "mcp" : name;
}