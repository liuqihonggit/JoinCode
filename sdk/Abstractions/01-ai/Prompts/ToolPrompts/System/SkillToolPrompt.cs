namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// SkillTool 提示词
/// 预算控制常量和 GetCharBudget 已统一到 SkillDescriptionTruncator
/// </summary>
[ToolPrompt(ToolName = "Skill", Category = ToolPromptCategory.System)]
public static class SkillToolPrompt
{
    public const string ToolName = SkillToolNameConstants.Skill;

    /// <summary>
    /// 获取 Skill 工具提示词
    /// </summary>
    public static string GetPrompt()
    {
        return """
            在主对话中执行技能

            当用户要求你执行任务时，检查是否有任何可用技能匹配。技能提供专门的能力和领域知识。

            当用户引用"斜杠命令"或"/<某物>"（例如，"/commit"、"/review-pr"）时，他们指的是技能。使用此工具调用它。

            如何调用：
            - 使用此工具，带上技能名称和可选参数
            - 示例：
              - `skill: "pdf"` - 调用 pdf 技能
              - `skill: "commit", args: "-m 'Fix bug'"` - 带参数调用
              - `skill: "review-pr", args: "123"` - 带参数调用
              - `skill: "ms-office-suite:pdf"` - 使用完全限定名调用

            重要：
            - 可用技能列在对话中的 system-reminder 消息中
            - 当技能匹配用户请求时，这是一个阻塞要求：在生成关于任务的任何其他响应之前，先调用相关 Skill 工具
            - 永远不要提及技能而不实际调用此工具
            - 不要调用已在运行的技能
            - 不要将此工具用于内置 JoinCode 命令（如 /help、/clear 等）
            - 如果你在对话的当前轮次中看到 <command_name> 标签，技能已经加载 - 直接遵循说明，而不是再次调用此工具
            """;
    }

    /// <summary>
    /// 获取字符预算 — 委托到 SkillDescriptionTruncator.GetCharBudget
    /// </summary>
    public static int GetCharBudget(int? contextWindowTokens = null)
        => SkillDescriptionTruncator.GetCharBudget(contextWindowTokens);
}
