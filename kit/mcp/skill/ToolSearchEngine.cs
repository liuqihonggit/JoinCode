namespace McpClient;

/// <summary>
/// 工具搜索引擎 — 提供 select 精确选择、map 分组下钻、list_groups 列出分组、关键词模糊搜索等渐进式查询能力
/// </summary>
public sealed class ToolSearchEngine {
    private readonly List<DeferredToolInfo> _deferredTools;
    private readonly List<string[]> _namePartsList;

    /// <summary>
    /// 初始化 <see cref="ToolSearchEngine"/> 实例
    /// </summary>
    /// <param name="deferredTools">延迟加载的工具信息列表（可选）</param>
    public ToolSearchEngine(IReadOnlyList<DeferredToolInfo> deferredTools) {
        _deferredTools = deferredTools != null ? [.. deferredTools] : [];
        _namePartsList = _deferredTools.Select(t => t.Name.Split('.', '_')).ToList();
    }

    /// <summary>
    /// 渐进式查询 — 优先级：select 精确选择 → map 分组下钻 → list_groups 列出分组 → 关键词搜索
    /// </summary>
    /// <param name="query">查询字符串</param>
    /// <param name="maxResults">最大结果数（默认 10）</param>
    /// <returns>工具搜索结果</returns>
    public ToolSearchResult Search(string query, int maxResults = 10) {
        ArgumentException.ThrowIfNullOrEmpty(query);

        var selectResult = TrySelect(query);
        if (selectResult != null)
            return selectResult;

        var mapResult = TryMap(query);
        if (mapResult != null)
            return mapResult;

        var groupResult = TryListGroups(query);
        if (groupResult != null)
            return groupResult;

        return KeywordSearch(query, maxResults);
    }

    /// <summary>
    /// 解析 map[主分组][子分组][工具名] 三级下钻语法
    /// </summary>
    private ToolSearchResult? TryMap(string query) {
        if (!query.StartsWith("map[", StringComparison.OrdinalIgnoreCase) || !query.EndsWith("]", StringComparison.Ordinal))
            return null;

        var inner = query["map[".Length..^1];
        if (string.IsNullOrWhiteSpace(inner))
            return null;

        var segments = inner.Split("][", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return null;

        var category = segments[0].Trim();
        if (segments.Length == 1) {
            var toolsInCategory = _deferredTools
                .Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (toolsInCategory.Count == 0)
                return null;

            var names = toolsInCategory
                .OrderBy(t => t.GroupName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.Name)
                .ToList();
            return new ToolSearchResult(names);
        }

        var groupName = segments[1].Trim();
        if (segments.Length == 2) {
            var toolsInGroup = _deferredTools
                .Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(t.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (toolsInGroup.Count == 0)
                return null;

            var names = toolsInGroup
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.Name)
                .ToList();
            return new ToolSearchResult(names);
        }

        var toolName = segments[2].Trim();
        var matched = _deferredTools
            .Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.GroupName, groupName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .ToList();
        return matched.Count > 0 ? new ToolSearchResult(matched) : null;
    }

    /// <summary>
    /// 解析 list_groups 语法 — 列出全部主分组 → 子分组层级
    /// </summary>
    private ToolSearchResult? TryListGroups(string query) {
        if (!query.Equals("list_groups", StringComparison.OrdinalIgnoreCase))
            return null;

        var names = _deferredTools
            .Select(t => $"{t.Category ?? "其他"}{(t.GroupName is not null ? $"/{t.GroupName}" : string.Empty)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count > 0 ? new ToolSearchResult(names) : null;
    }

    private ToolSearchResult? TrySelect(string query) {
        if (!query.StartsWith("select:", StringComparison.OrdinalIgnoreCase))
            return null;

        var names = query["select:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
            return null;

        var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var matched = _deferredTools
            .Where(t => nameSet.Contains(t.Name))
            .Select(t => t.Name)
            .ToList();

        return matched.Count > 0 ? new ToolSearchResult(matched) : null;
    }

    private ToolSearchResult KeywordSearch(string query, int maxResults) {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrEmpty(t))
            .ToArray();

        if (terms.Length == 0)
            return ToolSearchResult.Empty;

        var scored = new List<(DeferredToolInfo Tool, int Score)>();

        for (var i = 0; i < _deferredTools.Count; i++) {
            var score = ComputeScore(_deferredTools[i], _namePartsList[i], terms);
            if (score > 0)
                scored.Add((_deferredTools[i], score));
        }

        var results = scored
            .OrderByDescending(s => s.Score)
            .Take(maxResults)
            .Select(s => s.Tool.Name)
            .ToList();

        return new ToolSearchResult(results);
    }

    private static int ComputeScore(DeferredToolInfo tool, string[] nameParts, string[] terms) {
        var score = 0;
        var nameSpan = tool.Name.AsSpan();

        foreach (var term in terms) {
            var isRequired = term.StartsWith('+');
            var normalizedTerm = isRequired ? term.AsSpan(1) : term.AsSpan();
            if (normalizedTerm.IsEmpty)
                continue;

            if (nameSpan.Equals(normalizedTerm, StringComparison.OrdinalIgnoreCase)) {
                score += tool.IsMcp ? 12 : 10;
            } else if (nameSpan.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase)) {
                score += tool.IsMcp ? 6 : 5;
            } else if (MatchesAnyPart(nameParts, normalizedTerm)) {
                score += tool.IsMcp ? 6 : 5;
            } else if (tool.Description != null && tool.Description.AsSpan().Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase)) {
                score += 2;
            } else if (isRequired) {
                return 0;
            }
        }

        return score;
    }

    private static bool MatchesAnyPart(string[] parts, ReadOnlySpan<char> term) {
        foreach (var p in parts) {
            if (p.AsSpan().Equals(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}