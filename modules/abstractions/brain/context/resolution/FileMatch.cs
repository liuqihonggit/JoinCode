namespace JoinCode.Abstractions.Brain.Context.Resolution;

public sealed record FileMatch {
    /// <summary>获取文件路径。</summary>
    public required string FilePath { get; init; }

    /// <summary>获取匹配类型。</summary>
    public required ReferenceMatchType MatchType { get; init; }

    /// <summary>获取相关性评分。</summary>
    public required double RelevanceScore { get; init; }

    /// <summary>获取匹配描述。</summary>
    public string? MatchDescription { get; init; }

    /// <summary>创建文件匹配实例。</summary>
    public static FileMatch Create(string filePath, ReferenceMatchType matchType, double relevanceScore, string? description = null)
        => new() {
            FilePath = filePath,
            MatchType = matchType,
            RelevanceScore = relevanceScore,
            MatchDescription = description
        };
}