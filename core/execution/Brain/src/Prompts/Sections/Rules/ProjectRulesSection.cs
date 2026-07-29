
namespace Core.Prompts.Sections;

/// <summary>
/// 项目规则部分 - 从项目配置文件加载的规则
/// </summary>
[PromptSection(Name = "project_rules", Order = 7)]
public static class ProjectRulesSection {
    public static string? GetContent() {
        var projectRules = PromptConfigSnapshot.Current.ProjectRules;
        if (string.IsNullOrWhiteSpace(projectRules)) {
            return null;
        }

        return $"""
# 项目规则

以下规则来自项目配置文件（如 {AppDataConstants.AppDataFolder}/{AppDataConstants.RulesFolderName}/{AppDataConstants.ProjectRulesFileName}）：

{projectRules}

这些规则定义了该项目的特定约定和要求。在协助用户时，请始终遵循这些规则。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("project_rules", GetContent);
}
