
namespace JoinCode.Abstractions.Models.SkillSearch;

public sealed class SkillSearchQuery {
    /// <summary>获取搜索关键词。</summary>
    public string? Keyword { get; init; }
    /// <summary>获取标签列表。</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    /// <summary>获取分类。</summary>
    public string? Category { get; init; }
    /// <summary>获取是否启用模糊匹配。</summary>
    public bool FuzzyMatch { get; init; } = true;
    /// <summary>获取页码。</summary>
    public int Page { get; init; } = 1;
    /// <summary>获取每页大小。</summary>
    public int PageSize { get; init; } = 20;
    /// <summary>获取上下文提示。</summary>
    public string? ContextHint { get; init; }
}

public sealed class SkillSearchResult {
    /// <summary>获取技能名称。</summary>
    public required string SkillName { get; init; }
    /// <summary>获取技能描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取相关度评分。</summary>
    public required double RelevanceScore { get; init; }
    /// <summary>获取匹配类型。</summary>
    public required SkillMatchType MatchType { get; init; }
    /// <summary>获取标签列表。</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    /// <summary>获取分类。</summary>
    public string? Category { get; init; }
    /// <summary>获取匹配字段。</summary>
    public string? MatchedField { get; init; }
    /// <summary>获取高亮文本。</summary>
    public string? Highlight { get; init; }
}

public enum SkillMatchType {
    [EnumValue("exact_name")] ExactName = 0,
    [EnumValue("partial_name")] PartialName = 1,
    [EnumValue("tag_match")] TagMatch = 2,
    [EnumValue("description_keyword")] DescriptionKeyword = 3,
    [EnumValue("fuzzy_match")] FuzzyMatch = 4,
    [EnumValue("context_recommendation")] ContextRecommendation = 5
}