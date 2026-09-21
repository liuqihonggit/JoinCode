namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ToolSearchResult {
    /// <summary>获取匹配的工具名称列表。</summary>
    public IReadOnlyList<string> MatchedToolNames { get; }
    /// <summary>获取是否有匹配结果。</summary>
    public bool HasMatches => MatchedToolNames.Count > 0;

    /// <summary>构造工具搜索结果。</summary>
    public ToolSearchResult(IReadOnlyList<string> matchedToolNames) {
        MatchedToolNames = matchedToolNames ?? [];
    }

    /// <summary>获取空结果。</summary>
    public static ToolSearchResult Empty { get; } = new([]);
}