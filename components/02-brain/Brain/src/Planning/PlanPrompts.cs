namespace Core.Planning;

/// <summary>
/// 计划执行提示词 — 独立 LLM 调用的 system prompt
/// 消费者：PlanService
/// </summary>
[PromptTemplate(Name = "plan_execution", Category = PromptTemplateCategory.Plan, Description = "计划执行系统提示词模板", HasParameters = true)]
public static class PlanPrompts
{
    /// <summary>
    /// 动态构建计划执行系统提示词
    /// </summary>
    public static string BuildPlanExecutionSystemPrompt(IReadOnlyDictionary<string, List<(string Name, string Description)>> toolCategories)
    {
        var sb = new StringBuilder(2048);

        sb.AppendLine("您是一位专业的 AI 规划助手。您的任务是：");
        sb.AppendLine("1. 分析用户的请求");
        sb.AppendLine("2. 将其分解为分步计划");
        sb.AppendLine("3. 使用可用的工具和功能执行每个步骤");
        sb.AppendLine("4. 提供全面的结果");
        sb.AppendLine();
        sb.AppendLine("创建计划时：");
        sb.AppendLine("- 具体且可操作");
        sb.AppendLine("- 考虑步骤之间的依赖关系");
        sb.AppendLine("- 优雅地处理错误");
        sb.AppendLine("- 为每个步骤提供清晰的输出");
        sb.AppendLine();
        sb.AppendLine("可用工具：");

        foreach (var category in toolCategories.OrderBy(t => t.Key))
        {
            sb.AppendLine();
            sb.AppendLine($"【{GetCategoryDisplayName(category.Key)}】");
            foreach (var tool in category.Value.OrderBy(t => t.Name))
            {
                sb.AppendLine($"- {category.Key}.{tool.Name}: {tool.Description}");
            }
        }

        return sb.ToString();
    }

    private static string GetCategoryDisplayName(string category)
    {
        return category switch
        {
            "code_generation" => "代码生成",
            "code_analysis" => "代码分析",
            "code_execution" => "代码执行",
            _ => category
        };
    }
}
