namespace Core.Planning;

/// <summary>
/// 计划执行提示词 — 独立 LLM 调用的 system prompt
/// 消费者：PlanService
/// </summary>
[PromptTemplate(Name = "plan_execution", Category = PromptTemplateCategory.Plan, Description = "计划执行系统提示词模板", HasParameters = true)]
public static class PlanPrompts {
    /// <summary>
    /// 动态构建计划执行系统提示词
    /// </summary>
    public static string BuildPlanExecutionSystemPrompt(IReadOnlyDictionary<string, List<ToolCategoryEntry>> visibleToolCategories) {
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
        sb.AppendLine("需求落地四列验收表（强制）：");
        sb.AppendLine("针对用户提出的每条要求，计划中必须列出以下验收表：");
        sb.AppendLine();
        sb.AppendLine("| 基建实现 | 消费点 | 已实现 | 已验收 |");
        sb.AppendLine("|---------|--------|--------|--------|");
        sb.AppendLine("| 底层能力实现位置（类/模块/文件） | 上层调用/使用位置 | ✅/❌ | ✅/❌ + 验收记录 |");
        sb.AppendLine();
        sb.AppendLine("后两列为强制钩子：");
        sb.AppendLine("- 已实现：防止\"写了文档但代码没实现\"——必须实际编码完成才能标 ✅");
        sb.AppendLine("- 已验收：防止\"实现了但没验收\"——必须真实运行通过才能标 ✅");
        sb.AppendLine();
        sb.AppendLine("C/S 项目验收引导：");
        sb.AppendLine("- 若用户项目是 C/S 架构（客户端/服务端），验收方式 = 启动参数验收");
        sb.AppendLine("- 验收前先统一所有启动参数格式和返回格式，避免人和 AI 产生误会");
        sb.AppendLine("- 通过启动参数手动运行 exe 真实执行，记录实际输出结果");
        sb.AppendLine();
        sb.AppendLine("验收手法（已验收列）：");
        sb.AppendLine("- 通过标准：成功执行并输出有意义的结果（非空输出、非 Error、非超时）");
        sb.AppendLine("- 超过 30s 的功能必须在验收记录中备注耗时");
        sb.AppendLine();
        sb.AppendLine("纵深防御 — 逐级降级拦截（每层拦截一种绕过方式，附正确做法）：");
        sb.AppendLine("L1 跳过验收：若试图只列前两列就交付 → 禁止，四列必须全部填写，已实现/已验收不可留空");
        sb.AppendLine("L2 mock 代替：若用单元测试/mock 代替真实 exe → 禁止，单元测试不能验证完整启动链路（DI 卡死、JSON 宽容等），必须真实 exe 运行");
        sb.AppendLine("L3 声称未运行：若声称已验收但未实际运行 → 禁止，未运行就是 ❌，正确做法是实际启动 exe 用启动参数调用并记录输出");
        sb.AppendLine("L4 设计限制说辞：若用\"设计限制\"\"理论上可行\"\"应该没问题\"跳过 → 禁止，用不了就是 ❌，不能用说辞代替实际运行");
        sb.AppendLine("L5 格式不统一：若启动参数/返回格式不统一导致无法验收 → 先统一格式再验收，不要跳过");
        sb.AppendLine();
        sb.AppendLine("兜底：未真实运行 exe = ❌，无例外。任何说辞都不能代替实际运行。验收记录必须包含实际运行命令和真实输出。");
        sb.AppendLine();
        sb.AppendLine("可用工具：");

        foreach (var category in visibleToolCategories.OrderBy(t => t.Key)) {
            sb.AppendLine();
            sb.AppendLine($"【{GetCategoryDisplayName(category.Key)}】");
            foreach (var tool in category.Value.OrderBy(t => t.Name)) {
                sb.AppendLine($"- {category.Key}.{tool.Name}: {tool.Description}");
            }
        }

        return sb.ToString();
    }

    private static string GetCategoryDisplayName(string category) {
        return category switch {
            "code_generation" => "代码生成",
            "code_analysis" => "代码分析",
            "code_execution" => "代码执行",
            _ => category
        };
    }
}