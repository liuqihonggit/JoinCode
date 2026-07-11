using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// Skill部分 - 如何使用技能系统
/// </summary>
[PromptSection(Name = "skill", Order = 14)]
public static class SkillSection {
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("skill", () => {
            return """
# 使用技能

/<skill-name>（例如/commit）是用户调用用户可调用技能的简写。
执行时，技能会扩展为完整提示词。
使用SkillTool工具来执行它们。

重要提示：仅对SkillTool工具的用户可调用技能部分列出的技能使用SkillTool——不要猜测或使用内置CLI命令。
""";
        });
    }
}
