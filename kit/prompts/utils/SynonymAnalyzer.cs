
namespace Core.Prompts.Utils;

/// <summary>
/// 同义词匹配结果 — 描述一次同义词命中的键与补充内容。
/// </summary>
public sealed class SynonymMatchResult
{
    /// <summary>
    /// 匹配到的同义词键。
    /// </summary>
    public string MatchedKey { get; init; } = "";

    /// <summary>
    /// 匹配键对应的补充内容。
    /// </summary>
    public string SupplementaryContent { get; init; } = "";

    /// <summary>
    /// 是否命中同义词。当 <see cref="MatchedKey"/> 非空时为 true。
    /// </summary>
    public bool HasMatch => !string.IsNullOrEmpty(MatchedKey);
}

/// <summary>
/// 同义词分析器 — 基于 Aho-Corasick 自动机在输入文本中检测同义词并返回匹配结果。
/// </summary>
public static class SynonymAnalyzer
{
    /// <summary>
    /// 分析输入文本，检测同义词匹配。
    /// </summary>
    /// <param name="input">待分析文本。</param>
    /// <param name="synonymMap">同义词映射表。</param>
    /// <returns>匹配结果列表；输入为空或映射表无条目时返回空列表。</returns>
    public static IReadOnlyList<SynonymMatchResult> Analyze(string input, ISynonymMap synonymMap)
    {
        if (string.IsNullOrWhiteSpace(input) || synonymMap.Entries.Count == 0)
        {
            return [];
        }

        var ac = AhoCorasick<(string Key, string Content)>.Create(
            synonymMap.Entries
                .Where(static kv => !string.IsNullOrEmpty(kv.Key))
                .Select(static kv => new KeyValuePair<string, (string, string)>(kv.Key, (kv.Key, kv.Value))),
            ignoreCase: true);

        var matches = ac.FindAll(input.AsSpan());
        if (matches.Count == 0)
        {
            return [];
        }

        var results = new List<SynonymMatchResult>(matches.Count);
        foreach (var m in matches)
        {
            results.Add(new SynonymMatchResult
            {
                MatchedKey = m.Value.Key,
                SupplementaryContent = m.Value.Content
            });
        }

        return results;
    }
}
