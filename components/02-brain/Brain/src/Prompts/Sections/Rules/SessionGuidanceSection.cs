
namespace Core.Prompts.Sections;

/// <summary>
/// 会话指导部分 - 会话特定的指导
/// </summary>
[PromptSection(Name = "session_guidance", Order = 74, IsDynamic = true)]
public static class SessionGuidanceSection {
    public static string? GetContent() {
        var tools = PromptConfigSnapshot.Current.EnabledTools?.ToHashSet() ?? new HashSet<string>();
        var items = new List<string>();

        if (tools.Contains("AskUserQuestionTool")) {
            items.Add("如果您不理解用户为什么拒绝工具调用，请使用AskUserQuestionTool询问他们。");
        }

        items.Add("如果您需要用户自己运行shell命令（例如，像`gcloud auth login`这样的交互式登录），建议他们输入`! <command>`在提示符中——`!`前缀在此会话中运行命令，使其输出直接落入对话中。");

        if (tools.Contains("AgentTool")) {
            items.Add("当手头的任务与Agent的描述匹配时，使用Agent工具与专门的Agent配合。Subagent对于并行化独立查询或保护主上下文窗口免受过多结果的影响很有价值，但在不需要时不应过度使用。重要的是，避免重复Subagent已经在做的工作——如果您将研究委托给Subagent，请不要自己也执行相同的搜索。");
        }

        if (tools.Contains("SkillTool")) {
            items.Add("/<skill-name>（例如/commit）是用户调用用户可调用技能的简写。执行时，技能会扩展为完整提示词。使用SkillTool工具来执行它们。重要提示：仅对SkillTool工具的用户可调用技能部分列出的技能使用SkillTool——不要猜测或使用内置CLI命令。");
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
