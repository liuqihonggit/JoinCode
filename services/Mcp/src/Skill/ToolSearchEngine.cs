namespace McpClient;

public sealed class ToolSearchEngine
{
    private readonly List<DeferredToolInfo> _deferredTools;

    public ToolSearchEngine(IReadOnlyList<DeferredToolInfo> deferredTools)
    {
        _deferredTools = deferredTools != null ? [.. deferredTools] : [];
    }

    public ToolSearchResult Search(string query, int maxResults = 10)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);

        var selectResult = TrySelect(query);
        if (selectResult != null)
            return selectResult;

        return KeywordSearch(query, maxResults);
    }

    private ToolSearchResult? TrySelect(string query)
    {
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

    private ToolSearchResult KeywordSearch(string query, int maxResults)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrEmpty(t))
            .ToArray();

        if (terms.Length == 0)
            return ToolSearchResult.Empty;

        var scored = new List<(DeferredToolInfo Tool, int Score)>();

        foreach (var tool in _deferredTools)
        {
            var score = ComputeScore(tool, terms);
            if (score > 0)
                scored.Add((tool, score));
        }

        var results = scored
            .OrderByDescending(s => s.Score)
            .Take(maxResults)
            .Select(s => s.Tool.Name)
            .ToList();

        return new ToolSearchResult(results);
    }

    private static int ComputeScore(DeferredToolInfo tool, string[] terms)
    {
        var score = 0;
        var nameParts = tool.Name.Split('.', '_');

        foreach (var term in terms)
        {
            var isRequired = term.StartsWith('+');
            var normalizedTerm = isRequired ? term[1..] : term;

            if (string.IsNullOrEmpty(normalizedTerm))
                continue;

            if (tool.Name.Equals(normalizedTerm, StringComparison.OrdinalIgnoreCase))
            {
                score += tool.IsMcp ? 12 : 10;
            }
            else if (tool.Name.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase))
            {
                score += tool.IsMcp ? 6 : 5;
            }
            else if (nameParts.Any(p => p.Equals(normalizedTerm, StringComparison.OrdinalIgnoreCase)))
            {
                score += tool.IsMcp ? 6 : 5;
            }
            else if (tool.Description != null && tool.Description.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (isRequired)
            {
                return 0;
            }
        }

        return score;
    }
}
