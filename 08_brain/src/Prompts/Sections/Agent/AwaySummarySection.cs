namespace Core.Prompts.Sections;

[PromptSection(Name = "away_summary", Order = 51)]
public static class AwaySummarySection
{
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

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("away_summary", GetContent);
}
