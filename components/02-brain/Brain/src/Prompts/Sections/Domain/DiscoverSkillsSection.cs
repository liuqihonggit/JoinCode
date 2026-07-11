using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 发现技能部分 - 关于技能发现的指导
/// </summary>
[PromptSection(Name = "discover_skills", Order = 15)]
public static class DiscoverSkillsSection {
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("discover_skills", () => {
            return """
# 发现技能

技能是可重用的工作流，可以执行常见任务。使用 DiscoverSkills 工具来查找可能帮助您完成当前任务的技能。

当不确定如何完成特定任务时，或者当任务看起来是常见模式（如创建提交、运行测试、部署应用）时，请先检查是否有可用的技能。

技能可以节省时间并确保遵循最佳实践。
""";
        });
    }
}
