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

    /// <summary>树里每行的摘要上限 — 分组节点的 summary 是前 3 个工具描述的拼接，不截断会占满一行。</summary>
    private const int TreeSummaryLimit = 110;

    /// <summary>搜索结果里的摘要上限 — 比目录树宽松，但仍要留住「一行看清是什么」的定位作用。</summary>
    private const int ResultSummaryLimit = 180;

    /// <summary>渲染目录树 — 分类即目录，节点即叶子；分组节点附带本地实现覆盖率。</summary>
    public static string RenderTree(IEnumerable<CliToolDescriptor> nodes) {
        var sb = new StringBuilder();
        var list = nodes as IReadOnlyList<CliToolDescriptor> ?? nodes.ToList();
        AppendTotals(sb, list);
        var groups = list
            .GroupBy(node => node.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in groups) {
            var ordered = group.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
            // 单节点组（当前全部 13 组都是 1:1）：目录名就是节点 ID，再打一遍 "github/ —" 纯属噪音。
            if (ordered.Length == 1 && string.Equals(ordered[0].Id, group.Key, StringComparison.Ordinal)) {
                sb.AppendLine($"{group.Key}/ — {Brief(ordered[0].Summary, TreeSummaryLimit)}{Coverage(ordered[0])}");
                continue;
            }
            sb.AppendLine($"{group.Key}/");
            foreach (var node in ordered)
                sb.AppendLine($"  {node.Id}/ — {Brief(node.Summary, TreeSummaryLimit)}{Coverage(node)}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 总览行 —— 「546 个工具里到底隔离了多少」应当一眼可见，否则只能逐组把分母加起来。
    /// </summary>
    private static void AppendTotals(StringBuilder sb, IReadOnlyList<CliToolDescriptor> nodes) {
        var withTools = nodes.Where(node => node.HasToolEntries).ToList();
        if (withTools.Count == 0) return;
        var total = withTools.Sum(node => node.Tools.Length);
        var isolated = withTools.Sum(node => node.LocalToolCount);
        var percent = total == 0 ? 0 : isolated * 100 / total;
        sb.AppendLine(
            $"共 {withTools.Count} 组 / {total} 个工具，进程隔离 {isolated}（{percent}%：local 本 exe 内实现 + delegate 独立 exe）");
    }

    /// <summary>
    /// 摘要截断：目录树只用来定位节点，细节交给 `-h`。
    /// 不截断时 search 组那一行近千字符，等于把 3 个工具的描述塞进了「索引」。
    /// </summary>
    private static string Brief(string summary, int limit) {
        if (string.IsNullOrEmpty(summary) || summary.Length <= limit) return summary;
        return summary[..limit].TrimEnd() + "…";
    }

    /// <summary>渲染搜索结果 — 给出 -h 命令，引导 LLM 按需阅读而非一次性吞下全部描述。</summary>
    public static string RenderResults(IReadOnlyList<CliToolDescriptor> nodes, string? query = null) {
        if (nodes.Count == 0) return "未找到匹配的 CLI 工具节点。";
        var sb = new StringBuilder();
        sb.AppendLine($"命中 {nodes.Count} 个节点（用 <exe> -h 查看完整参数）:");
        foreach (var node in nodes) {
            sb.AppendLine($"  {node.Id,-14} [{node.Category}] {Brief(node.Summary, ResultSummaryLimit)}{Coverage(node)}");
            // 写出真实 exe 名：LLM 只看「<exe>」占位符时仍要去猜节点对应的可执行文件。
            if (!string.IsNullOrWhiteSpace(node.Exe)) sb.AppendLine($"       {node.Exe} -h");
            AppendMatchedTools(sb, node, query);
        }
        sb.AppendLine("调用方式: jcc tool call <节点ID> <工具名> [--参数 值]；阅读参数: jcc tool help <节点ID> 或 <exe> -h");
        return sb.ToString().TrimEnd();
    }

    /// <summary>进程隔离覆盖率（local + delegate）— 只有分组节点才有意义。</summary>
    private static string Coverage(CliToolDescriptor node) {
        if (!node.HasToolEntries) return string.Empty;
        var total = node.Tools.Length;
        var isolated = node.LocalToolCount;
        // 「进程隔离 0/60」会被读成「这组不能用」；它其实 60 个都能用，只是都走宿主。
        if (isolated == 0) return $"  (全部 {total} 个转发宿主)";
        if (isolated == total) return $"  (全部 {total} 个进程隔离)";
        return $"  (进程隔离 {isolated}/{total}，其余转发宿主)";
    }

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
            sb.AppendLine($"      · {entry.Id,-30} {ModeText(entry)}{Aliases(entry)}");
    }

    private static string ModeText(CliToolEntry entry) => entry.Mode switch {
        CliExecutionMode.Local => "本地进程隔离",
        CliExecutionMode.Delegate => "委托独立 exe（不经宿主）",
        _ => "转发宿主执行"
    };

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
