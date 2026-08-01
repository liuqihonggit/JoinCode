namespace Core.Prompts.Utils;

/// <summary>
/// 动态关键词词表配置模型 — 从 ~/.jcc/keyword-sections.json 加载
/// </summary>
public sealed record DynamicKeywordConfig
{
    /// <summary>
    /// 关键词 Section 配置字典 — Key 为 Section 名称，Value 为关键词列表
    /// </summary>
    public Dictionary<string, DynamicKeywordSection> Sections { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 单个关键词 Section 配置
/// </summary>
public sealed record DynamicKeywordSection
{
    /// <summary>
    /// 触发关键词列表（最小词根，如 "睡觉" 而非 "我去睡觉了"）
    /// </summary>
    public List<string> Keywords { get; init; } = [];

    /// <summary>
    /// 是否启用，默认 true
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 自定义注入内容（可选）— 为空时使用内置 Section 内容
    /// </summary>
    public string? CustomContent { get; init; }
}

/// <summary>
/// 动态关键词匹配器 — 纯逻辑，可独立测试
/// </summary>
public static class DynamicKeywordMatcher
{
    /// <summary>
    /// 在给定配置中匹配用户输入的关键词
    /// </summary>
    public static DynamicKeywordMatchResult? TryMatch(string input, DynamicKeywordConfig config)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var lowerInput = input.ToLowerInvariant();

        foreach (var (sectionName, section) in config.Sections)
        {
            if (!section.Enabled || section.Keywords.Count == 0)
                continue;

            foreach (var keyword in section.Keywords)
            {
                if (string.IsNullOrEmpty(keyword))
                    continue;

                if (lowerInput.Contains(keyword.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                {
                    return new DynamicKeywordMatchResult
                    {
                        SectionName = sectionName,
                        MatchedKeyword = keyword,
                        CustomContent = section.CustomContent
                    };
                }
            }
        }

        return null;
    }
}
