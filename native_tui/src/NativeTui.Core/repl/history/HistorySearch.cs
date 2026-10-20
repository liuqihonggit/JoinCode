namespace NativeTui.Core.Repl;

/// <summary>
/// 输入历史反向搜索（Ctrl+R）— 对齐基准 ctrl+r 搜索（getTimestampedHistory 语义）：
/// 子串匹配（大小写不敏感）+ 按文本去重 + 新→旧排序 + 容量 100 截断；
/// 查询变化重置到最新匹配；Next 向旧循环（越界回绕最新）；HistoryRing 注入旧→新序。
/// </summary>
public sealed class HistorySearch
{
    /// <summary>匹配窗口容量（基准 MAX_HISTORY_ITEMS）。</summary>
    public const int Capacity = 100;

    /// <summary>当前查询。</summary>
    public string Query { get; private set; } = string.Empty;

    /// <summary>匹配列表（新→旧，已去重截断）。</summary>
    public IReadOnlyList<string> Matches { get; private set; } = [];

    /// <summary>当前选中匹配索引（0 = 最新）。</summary>
    public int MatchIndex { get; private set; }

    /// <summary>当前选中匹配（无匹配为空串）。</summary>
    public string Current => MatchIndex < Matches.Count ? Matches[MatchIndex] : string.Empty;

    private readonly IReadOnlyList<string> _history;

    private HistorySearch(IReadOnlyList<string> history)
    {
        _history = history;
    }

    /// <summary>进入搜索 — history 为旧→新序（HistoryRing.Entries 注入）；查询变化经 UpdateQuery 重过滤。</summary>
    public static HistorySearch Begin(string query, IReadOnlyList<string> history)
    {
        var search = new HistorySearch(history);
        search.UpdateQuery(query);
        return search;
    }

    /// <summary>查询变化 — 重新过滤并重置到最新匹配（新条目入史后即时生效 — 引用同源）。</summary>
    public void UpdateQuery(string query)
    {
        Query = query;
        Matches = Filter(query, _history);
        MatchIndex = 0;
    }

    /// <summary>下一个匹配（向旧方向；越过最旧回绕最新）。</summary>
    public void Next()
    {
        if (Matches.Count == 0)
        {
            return;
        }

        MatchIndex = (MatchIndex + 1) % Matches.Count;
    }

    /// <summary>上一个匹配（向新方向；越过最新回绕最旧 — Up 导航对称）。</summary>
    public void Previous()
    {
        if (Matches.Count == 0)
        {
            return;
        }

        MatchIndex = (MatchIndex - 1 + Matches.Count) % Matches.Count;
    }

    /// <summary>过滤 — 新→旧遍历（history 旧→新倒序）、子串大小写不敏感、文本去重、容量截断。</summary>
    private static List<string> Filter(string query, IReadOnlyList<string> history)
    {
        var seen = new HashSet<string>();
        var matches = new List<string>();
        for (var i = history.Count - 1; i >= 0 && matches.Count < Capacity; i--)
        {
            var entry = history[i];
            if (!entry.Contains(query, StringComparison.OrdinalIgnoreCase) || !seen.Add(entry))
            {
                continue;
            }

            matches.Add(entry);
        }

        return matches;
    }
}
