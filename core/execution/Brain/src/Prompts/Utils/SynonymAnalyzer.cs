
namespace Core.Prompts.Utils;

public sealed class SynonymMatchResult
{
    public string MatchedKey { get; init; } = "";
    public string SupplementaryContent { get; init; } = "";
    public bool HasMatch => !string.IsNullOrEmpty(MatchedKey);
}

public static class SynonymAnalyzer
{
    public static IReadOnlyList<SynonymMatchResult> Analyze(string input, ISynonymMap synonymMap)
    {
        if (string.IsNullOrWhiteSpace(input) || synonymMap.Entries.Count == 0)
        {
            return [];
        }

        var ac = AhoCorasick<string>.Create(
            synonymMap.Entries
                .Where(static kv => !string.IsNullOrEmpty(kv.Key))
                .Select(static kv => new KeyValuePair<string, string>(kv.Key, kv.Value)),
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
                MatchedKey = input.AsSpan().Slice(m.StartIndex, m.Length).ToString(),
                SupplementaryContent = m.Value
            });
        }

        return results;
    }
}
