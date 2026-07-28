
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

        var results = new List<SynonymMatchResult>();
        var lowerInput = input.ToLowerInvariant();

        foreach (var (key, supplementaryContent) in synonymMap.Entries)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (lowerInput.Contains(key.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new SynonymMatchResult
                {
                    MatchedKey = key,
                    SupplementaryContent = supplementaryContent
                });
            }
        }

        return results;
    }
}
