
namespace Core.Prompts.Sections;

/// <summary>
/// 会话指导部分 - 会话特定的指导
/// </summary>
[PromptSection(Name = "session_guidance", Order = 74, IsDynamic = true)]
public static class SessionGuidanceSection {
    /// <summary>
    /// 获取 session_guidance 部分内容；根据已启用工具动态拼接的会话指导，无内容时返回 null。
    /// </summary>
    public static string? GetContent() {
        var tools = PromptConfigSnapshot.Current.EnabledTools.ToHashSet();
        var items = new List<string>();

        if (tools.Contains(InteractionToolNameEnumConstants.AskUserQuestion)) {
            items.Add($"如果您不理解用户为什么拒绝工具调用，请使用{InteractionToolNameEnumConstants.AskUserQuestion}询问他们。");
        }

        items.Add("如果您需要用户自己运行shell命令（例如，像`gcloud auth login`这样的交互式登录），建议他们输入`! <command>`在提示符中——`!`前缀在此会话中运行命令，使其输出直接落入对话中。");

        items.Add("不无脑附和用户错误观点。不必保持客观态度，但可以尽你所能预判任何技术难题和天坑。不要错误引导用户到一个当前便宜但未来难维护的方案。要知道用户可能是扮演小白来考验你。你必须细腻应对未来，保持未来脚本替换目标处理的便利性。");

        if (tools.Contains(AgentToolNameEnumConstants.Agent)) {
            items.Add($"当手头的任务与{AgentToolNameEnumConstants.Agent}的描述匹配时，使用{AgentToolNameEnumConstants.Agent}工具与专门的{AgentToolNameEnumConstants.Agent}配合。{AgentToolSection.SubagentUsageGuidance}");
        }

        if (tools.Contains(SkillToolNameEnumConstants.Skill)) {
            items.Add($"/<skill-name>（例如/commit）是用户调用用户可调用技能的简写。执行时，技能会扩展为完整提示词。使用{SkillToolNameEnumConstants.Skill}工具来执行它们。重要提示：仅对{SkillToolNameEnumConstants.Skill}工具的用户可调用技能部分列出的技能使用{SkillToolNameEnumConstants.Skill}——不要猜测或使用内置CLI命令。");
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

    /// <summary>
    /// 创建 session_guidance 提示词部分。
    /// </summary>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("session_guidance", GetContent);
}