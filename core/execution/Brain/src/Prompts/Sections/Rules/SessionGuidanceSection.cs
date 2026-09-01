
namespace Core.Prompts.Sections;

/// <summary>
/// 会话指导部分 - 会话特定的指导
/// </summary>
[PromptSection(Name = "session_guidance", Order = 74, IsDynamic = true)]
public static class SessionGuidanceSection {
    public static string? GetContent() {
        var tools = PromptConfigSnapshot.Current.EnabledTools.ToHashSet();
        var items = new List<string>();

        if (tools.Contains(InteractionToolNameConstants.AskUserQuestion)) {
            items.Add($"如果您不理解用户为什么拒绝工具调用，请使用{InteractionToolNameConstants.AskUserQuestion}询问他们。");
        }

        items.Add("如果您需要用户自己运行shell命令（例如，像`gcloud auth login`这样的交互式登录），建议他们输入`! <command>`在提示符中——`!`前缀在此会话中运行命令，使其输出直接落入对话中。");

        if (tools.Contains(AgentToolNameConstants.Agent)) {
            items.Add($"当手头的任务与{AgentToolNameConstants.Agent}的描述匹配时，使用{AgentToolNameConstants.Agent}工具与专门的{AgentToolNameConstants.Agent}配合。{AgentToolSection.SubagentUsageGuidance}");
        }

        if (tools.Contains(SkillToolNameConstants.Skill)) {
            items.Add($"/<skill-name>（例如/commit）是用户调用用户可调用技能的简写。执行时，技能会扩展为完整提示词。使用{SkillToolNameConstants.Skill}工具来执行它们。重要提示：仅对{SkillToolNameConstants.Skill}工具的用户可调用技能部分列出的技能使用{SkillToolNameConstants.Skill}——不要猜测或使用内置CLI命令。");
        }

        if (items.Count == 0) {
            return null;
        }

        var result = new System.Text.StringBuilder();
        result.AppendLine("# 会话特定指导");
        foreach (var item in items) {
            result.AppendLine($" - {item}");
        }

        return result.ToString().TrimEnd();
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("session_guidance", GetContent);
}
