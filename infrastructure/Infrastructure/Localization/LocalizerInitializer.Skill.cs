namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterSkillEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === SkillSearchToolHandlers ===
        defaultEntries[StringKey.SkillSearchResult] = "Skill search results: {0} matches";
        defaultEntries[StringKey.NoMatchingSkillFound] = "No matching skills found";
        defaultEntries[StringKey.LabelRelevance] = "Relevance: {0}";
        defaultEntries[StringKey.LabelTags] = "Tags: {0}";
        defaultEntries[StringKey.SkillSearchFailedLog] = "Skill search failed";
        defaultEntries[StringKey.SkillSearchFailed] = "Skill search failed: {0}";
        defaultEntries[StringKey.ContextDescriptionCannotBeEmpty] = "Context description cannot be empty";
        defaultEntries[StringKey.SkillRecommendResult] = "Skill recommendation results: {0} recommendations";
        defaultEntries[StringKey.NoRecommendedSkillFound] = "No recommended skills found";
        defaultEntries[StringKey.SkillRecommendFailedLog] = "Skill recommendation failed";
        defaultEntries[StringKey.SkillRecommendFailed] = "Skill recommendation failed: {0}";

        zhEntries[StringKey.SkillSearchResult] = "技能搜索结果: {0} 个匹配";
        zhEntries[StringKey.NoMatchingSkillFound] = "未找到匹配的技能";
        zhEntries[StringKey.LabelRelevance] = "相关度: {0}";
        zhEntries[StringKey.LabelTags] = "标签: {0}";
        zhEntries[StringKey.SkillSearchFailedLog] = "技能搜索失败";
        zhEntries[StringKey.SkillSearchFailed] = "技能搜索失败: {0}";
        zhEntries[StringKey.ContextDescriptionCannotBeEmpty] = "上下文描述不能为空";
        zhEntries[StringKey.SkillRecommendResult] = "技能推荐结果: {0} 个推荐";
        zhEntries[StringKey.NoRecommendedSkillFound] = "未找到推荐技能";
        zhEntries[StringKey.SkillRecommendFailedLog] = "技能推荐失败";
        zhEntries[StringKey.SkillRecommendFailed] = "技能推荐失败: {0}";

        // === ToolSearchToolHandlers ===
        defaultEntries[StringKey.ToolSearchQueryCannotBeEmpty] = "query cannot be empty";
        defaultEntries[StringKey.ToolSearchNoMatchingTools] = "No matching tools found";
        defaultEntries[StringKey.ToolSearchRegisteredToolsCount] = "There are {0} registered tools available";
        defaultEntries[StringKey.ToolSearchMatchedCount] = "Matched {0} tools (out of {1})";
        defaultEntries[StringKey.ToolSearchFailed] = "Tool search failed: {0}";
        defaultEntries[StringKey.ToolSearchFailedLog] = "Tool search failed";
        defaultEntries[StringKey.ToolSearchResultTitle] = "Tool search results (query: {0})";

        zhEntries[StringKey.ToolSearchQueryCannotBeEmpty] = "query 不能为空";
        zhEntries[StringKey.ToolSearchNoMatchingTools] = "未找到匹配的工具";
        zhEntries[StringKey.ToolSearchRegisteredToolsCount] = "共有 {0} 个已注册的工具可用";
        zhEntries[StringKey.ToolSearchMatchedCount] = "匹配 {0} 个工具 (共 {1} 个)";
        zhEntries[StringKey.ToolSearchFailed] = "工具搜索失败: {0}";
        zhEntries[StringKey.ToolSearchFailedLog] = "工具搜索失败";
        zhEntries[StringKey.ToolSearchResultTitle] = "工具搜索结果 (查询: {0})";

        // === SkillToolHandlers ===
        defaultEntries[StringKey.SkillNameCannotBeEmpty] = "Skill name cannot be empty";
        defaultEntries[StringKey.SkillNotFound] = "Skill not found: {0}";
        defaultEntries[StringKey.LabelSkill] = "Skill: {0}";
        defaultEntries[StringKey.LabelExecutionTime] = "Execution time: {0}ms";
        defaultEntries[StringKey.LabelOutput] = "[Output]";
        defaultEntries[StringKey.SkillExecutionSuccessNoOutput] = "Skill executed successfully (no output)";
        defaultEntries[StringKey.LabelError] = "[Error]";
        defaultEntries[StringKey.SkillExecutionFailed] = "Skill execution failed";
        defaultEntries[StringKey.AvailableSkills] = "Available skills: {0}";
        defaultEntries[StringKey.Uncategorized] = "Uncategorized";
        defaultEntries[StringKey.LabelParameters] = "Parameters: {0}";
        defaultEntries[StringKey.NoAvailableSkills] = "No available skills";

        zhEntries[StringKey.SkillNameCannotBeEmpty] = "技能名称不能为空";
        zhEntries[StringKey.SkillNotFound] = "技能不存在: {0}";
        zhEntries[StringKey.LabelSkill] = "技能: {0}";
        zhEntries[StringKey.LabelExecutionTime] = "执行耗时: {0}ms";
        zhEntries[StringKey.LabelOutput] = "[输出]";
        zhEntries[StringKey.SkillExecutionSuccessNoOutput] = "技能执行成功（无输出）";
        zhEntries[StringKey.LabelError] = "[错误]";
        zhEntries[StringKey.SkillExecutionFailed] = "技能执行失败";
        zhEntries[StringKey.AvailableSkills] = "可用技能: {0} 个";
        zhEntries[StringKey.Uncategorized] = "未分类";
        zhEntries[StringKey.LabelParameters] = "参数: {0}";
        zhEntries[StringKey.NoAvailableSkills] = "暂无可用技能";
    }
}
