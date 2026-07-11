namespace JoinCode.Abstractions.Brain.Context.Resolution;

public sealed record CodeReference
{
    public required string ReferencePath { get; init; }

    public required string ResolvedPath { get; init; }

    public required ReferenceMatchType MatchType { get; init; }

    public required double RelevanceScore { get; init; }

    public required IReadOnlyList<FileMatch> FileMatches { get; init; }

    public bool IsResolved => FileMatches.Count > 0 && RelevanceScore > 0;

    public static CodeReference Unresolved(string referencePath)
        => new()
        {
            ReferencePath = referencePath,
            ResolvedPath = string.Empty,
            MatchType = ReferenceMatchType.Partial,
            RelevanceScore = 0,
            FileMatches = Array.Empty<FileMatch>()
        };

    public static CodeReference ExactMatch(string referencePath, string resolvedPath, IReadOnlyList<FileMatch> matches)
        => new()
        {
            ReferencePath = referencePath,
            ResolvedPath = resolvedPath,
            MatchType = ReferenceMatchType.Exact,
            RelevanceScore = 1.0,
            FileMatches = matches
        };
}
