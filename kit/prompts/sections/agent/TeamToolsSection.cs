namespace Core.Prompts.Sections;

/// <summary>
/// 团队工具提示词部分 — 注入 TeamCreate/TeamDelete/SendMessage 的完整使用指南
/// </summary>
[PromptSection(Name = "team_tools", Order = 13)]
public static class TeamToolsSection
{
    /// <summary>
    /// 获取 team_tools 部分内容；拼接团队创建/删除与消息发送工具的使用指南。
    /// </summary>
    public static string? GetContent()
    {
        var hasTeamTools = PromptConfigSnapshot.Current.HasTeamTools;
        var hasSendMessage = PromptConfigSnapshot.Current.HasSendMessage;

        if (!hasTeamTools && !hasSendMessage)
            return string.Empty;

        var sb = new System.Text.StringBuilder();

        if (hasTeamTools)
        {
            sb.AppendLine(TeamCreateToolPrompt.Prompt);
            sb.AppendLine();
            sb.AppendLine(TeamDeleteToolPrompt.Prompt);
        }

        if (hasSendMessage)
        {
            sb.AppendLine();
            sb.AppendLine(SendMessageToolPrompt.GetPrompt());
        }

        return sb.ToString();
    }

    /// <summary>
    /// 创建 team_tools 提示词部分。
    /// </summary>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("team_tools", GetContent);
}
