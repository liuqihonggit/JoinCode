namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 工具名解析与推荐 — 从 ToolCallRepairService 提取的单一职责小类
/// <para>职责: 工具名归一化(大小写/下划线模糊匹配) + 相似工具名推荐(Levenshtein编辑距离)</para>
/// </summary>
internal static class ToolNameResolver
{
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
}
