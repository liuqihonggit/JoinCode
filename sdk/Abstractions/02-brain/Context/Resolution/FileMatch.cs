namespace JoinCode.Abstractions.Brain.Context.Resolution;

public sealed record FileMatch
{
    public required string FilePath { get; init; }

    public required ReferenceMatchType MatchType { get; init; }

    public required double RelevanceScore { get; init; }

    public string? MatchDescription { get; init; }

    public static FileMatch Create(string filePath, ReferenceMatchType matchType, double relevanceScore, string? description = null)
        => new()
        {
            FilePath = filePath,
            MatchType = matchType,
            RelevanceScore = relevanceScore,
            MatchDescription = description
        };
}
