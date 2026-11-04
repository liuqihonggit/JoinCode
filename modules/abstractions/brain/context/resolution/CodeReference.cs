namespace JoinCode.Abstractions.Brain.Context.Resolution;

public sealed record CodeReference {
    /// <summary>获取引用路径。</summary>
    public required string ReferencePath { get; init; }

    /// <summary>获取解析后路径。</summary>
    public required string ResolvedPath { get; init; }

    /// <summary>获取匹配类型。</summary>
    public required ReferenceMatchType MatchType { get; init; }

    /// <summary>获取相关性分数。</summary>
    public required double RelevanceScore { get; init; }

    /// <summary>获取文件匹配列表。</summary>
    public required IReadOnlyList<FileMatch> FileMatches { get; init; }

    /// <summary>获取是否已解析。</summary>
    public bool IsResolved => FileMatches.Count > 0 && RelevanceScore > 0;

    /// <summary>创建未解析的代码引用。</summary>
    public static CodeReference Unresolved(string referencePath)
        => new() {
            ReferencePath = referencePath,
            ResolvedPath = string.Empty,
            MatchType = ReferenceMatchType.Partial,
            RelevanceScore = 0,
            FileMatches = Array.Empty<FileMatch>()
        };

    /// <summary>创建精确匹配的代码引用。</summary>
    public static CodeReference ExactMatch(string referencePath, string resolvedPath, IReadOnlyList<FileMatch> matches)
        => new() {
            ReferencePath = referencePath,
            ResolvedPath = resolvedPath,
            MatchType = ReferenceMatchType.Exact,
            RelevanceScore = 1.0,
            FileMatches = matches
        };
}