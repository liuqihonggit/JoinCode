namespace JoinCode.Gui.SlashCommands;

/// <summary>
/// 斜杠命令排序器 — 对 Trie 匹配结果按前缀匹配度与高频权重排序。
/// 规则：完全匹配优先 → 高频权重降序 → 命令名长度升序 → 字母序兜底。
/// </summary>
public static class SlashCommandRanker
{
    /// <summary>
    /// 对候选命令排序。
    /// </summary>
    /// <param name="candidates">Trie 匹配出的候选命令</param>
    /// <param name="prefix">当前输入前缀（含 /）</param>
    /// <param name="weights">命令权重表（key 为命令名含 /，value 为使用频率/权重）；null 时无权重</param>
    /// <returns>排序后的候选列表</returns>
    public static IReadOnlyList<SlashCommandItem> Rank(
        IReadOnlyList<SlashCommandItem> candidates,
        string prefix,
        IReadOnlyDictionary<string, int>? weights = null)
    {
        if (candidates.Count <= 1)
            return candidates;

        var prefixOrd = StringComparer.OrdinalIgnoreCase;
        return candidates
            .OrderByDescending(c => prefixOrd.Equals(c.Name, prefix))
            .ThenByDescending(c => weights is not null && weights.TryGetValue(c.Name, out var w) ? w : 0)
            .ThenBy(c => c.Name.Length)
            .ThenBy(c => c.Name, prefixOrd)
            .ToList();
    }
}
