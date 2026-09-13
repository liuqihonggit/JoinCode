namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ToolSearchResult
{
    public IReadOnlyList<string> MatchedToolNames { get; }
    public bool HasMatches => MatchedToolNames.Count > 0;

    public ToolSearchResult(IReadOnlyList<string> matchedToolNames)
    {
        MatchedToolNames = matchedToolNames ?? [];
    }

    public static ToolSearchResult Empty { get; } = new([]);
}
