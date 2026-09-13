namespace Core.Prompts.Sections;

/// <summary>
/// 用户离开期间活动摘要的提示词部分。
/// </summary>
[PromptSection(Name = "away_summary", Order = 51)]
public static class AwaySummarySection
{
    /// <summary>
    /// 获取 away_summary 部分内容；用户离开期间的活动摘要，无摘要时返回 null。
    /// </summary>
    public static string? GetContent()
    {
        var summary = PromptConfigSnapshot.Current.AwaySummary;
        if (string.IsNullOrEmpty(summary)) return null;
        return $"""
<away_summary>
用户之前离开了会话，以下是离开期间的活动摘要：
{summary}
请基于此摘要继续对话，无需重复已处理的内容。
</away_summary>
""";
    }

    /// <summary>
    /// 创建 away_summary 提示词部分。
    /// </summary>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("away_summary", GetContent);
}
