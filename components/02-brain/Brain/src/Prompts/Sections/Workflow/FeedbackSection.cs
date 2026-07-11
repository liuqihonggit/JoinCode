using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 反馈部分 - 如何向用户提供反馈渠道
/// </summary>
[PromptSection(Name = "feedback", Order = 26)]
public static class FeedbackSection {
    public static string? GetContent() {
        var issuesExplainer = PromptConfigSnapshot.Current.IssuesExplainer;
        var feedbackChannel = PromptConfigSnapshot.Current.FeedbackChannel;
        var items = new List<string>();

        if (!string.IsNullOrWhiteSpace(issuesExplainer)) {
            items.Add($"要提供反馈，用户应该{issuesExplainer}");
        }

        if (!string.IsNullOrWhiteSpace(feedbackChannel)) {
            items.Add($"反馈渠道：{feedbackChannel}");
        }

        // 默认反馈指导
        items.Add("如果用户报告Claude Code本身的错误、缓慢或意外行为（而不是要求您修复他们自己的代码），推荐适当的命令：/issue 用于模型相关问题（奇怪输出、错误工具选择、幻觉、拒绝），或 /share 用于上传完整会话记录以报告产品错误、崩溃、缓慢或一般问题。仅在用户描述Claude Code问题时推荐这些。");

        if (items.Count == 0) {
            return null;
        }

        var result = new System.Text.StringBuilder();
        result.AppendLine("# 反馈");
        result.AppendLine("如果用户寻求帮助或想要提供反馈，请告诉他们以下信息：");
        foreach (var item in items) {
            result.AppendLine($"   - {item}");
        }

        return result.ToString().TrimEnd();
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("feedback", GetContent);
}
