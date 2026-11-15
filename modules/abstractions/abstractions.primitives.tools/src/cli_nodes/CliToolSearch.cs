using System.Text;

namespace JoinCode.Abstractions.Tools;

/// <summary>
/// CLI 层搜索下钻 — 取代 MCP 的 tool_groups 三级 map[] 语法。
/// 目录树由 category 表达，搜索命中 id / name / summary / tags / category，查完即弃不进上下文。
/// </summary>
public static class CliToolSearch {
    /// <summary>按关键词查找节点，按相关度排序；空查询返回全部（按 id 稳定排序）。</summary>
    public static IReadOnlyList<CliToolDescriptor> Find(IEnumerable<CliToolDescriptor> nodes, string query) {
        if (string.IsNullOrWhiteSpace(query))
            return nodes.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return nodes
            .Select(node => (Node: node, Score: terms.Sum(term => ScoreTerm(node, term))))
            .Where(pair => pair.Score > 0)
            .OrderByDescending(pair => pair.Score)
            .ThenBy(pair => pair.Node.Id, StringComparer.Ordinal)
            .Select(pair => pair.Node)
            .ToArray();
    }

    /// <summary>渲染目录树 — 分类即目录，节点即叶子；分组节点附带本地实现覆盖率。</summary>
    public static string RenderTree(IEnumerable<CliToolDescriptor> nodes) {
        var sb = new StringBuilder();
        var groups = nodes
            .GroupBy(node => node.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in groups) {
            sb.AppendLine($"{group.Key}/");
            foreach (var node in group.OrderBy(item => item.Id, StringComparer.Ordinal))
                sb.AppendLine($"  {node.Id}/ — {node.Summary}{Coverage(node)}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>渲染搜索结果 — 给出 -h 命令，引导 LLM 按需阅读而非一次性吞下全部描述。</summary>
    public static string RenderResults(IReadOnlyList<CliToolDescriptor> nodes, string? query = null) {
        if (nodes.Count == 0) return "未找到匹配的 CLI 工具节点。";
        var sb = new StringBuilder();
        sb.AppendLine($"命中 {nodes.Count} 个节点（用 <exe> -h 查看完整参数）:");
        foreach (var node in nodes) {
            sb.AppendLine($"  {node.Id,-14} [{node.Category}] {node.Summary}{Coverage(node)}");
            AppendMatchedTools(sb, node, query);
        }
        sb.AppendLine("调用方式: jcc tool call <节点ID> <工具名> [--参数 值]；阅读参数: jcc tool help <节点ID> 或 <exe> -h");
        return sb.ToString().TrimEnd();
    }

    /// <summary>本地实现覆盖率 — 只有分组节点才有意义。</summary>
    private static string Coverage(CliToolDescriptor node) =>
        node.HasToolEntries ? $"  (本地实现 {node.LocalToolCount}/{node.Tools.Length})" : string.Empty;

    /// <summary>
    /// 查询命中分组内某个子工具时，列出命中的子工具及其执行方式 —
    /// 让 LLM 一次就知道「工具在哪个组、是否真进程隔离」，不必再猜或再搜一轮。
    /// </summary>
    private static void AppendMatchedTools(StringBuilder sb, CliToolDescriptor node, string? query) {
        if (!node.HasToolEntries || string.IsNullOrWhiteSpace(query)) return;
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matched = node.Tools
            .Where(entry => terms.Any(term => Contains(entry.Id, term)
                                              || entry.Aliases.Any(alias => Contains(alias, term))))
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
        foreach (var entry in matched)
            sb.AppendLine($"      · {entry.Id,-30} {(entry.IsLocal ? "本地进程隔离" : "转发宿主执行")}{Aliases(entry)}");
    }

    private static string Aliases(CliToolEntry entry) =>
        entry.Aliases.Length == 0 ? string.Empty : $"  别名: {string.Join(", ", entry.Aliases)}";

    private static int ScoreTerm(CliToolDescriptor node, string term) =>
        (node.Tools.Any(entry => Contains(entry.Id, term)
                                 || entry.Aliases.Any(alias => Contains(alias, term))) ? 120 : 0)
        + (Contains(node.Id, term) ? 100 : 0)
        + (Contains(node.Name, term) ? 50 : 0)
        + (Contains(node.Summary, term) ? 20 : 0)
        + (node.Tags.Any(tag => Contains(tag, term)) ? 10 : 0)
        + (Contains(node.Category, term) ? 5 : 0);

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
