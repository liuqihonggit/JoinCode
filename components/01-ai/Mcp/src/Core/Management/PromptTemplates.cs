

namespace McpToolHandlers;

/// <summary>
/// MCP 工作流提示词模板集合 — 工作流操作的本地化提示词构建
/// </summary>
[PromptTemplate(Name = "mcp_workflow", Category = PromptTemplateCategory.Mcp, Description = "MCP工作流提示词模板集合（执行/计划/代码生成/代码分析/对话）", HasParameters = true)]
public static class PromptTemplates
{
    // 使用 FrozenDictionary 提供只读高效访问，键为 AnalysisType 枚举
    public static readonly FrozenDictionary<AnalysisType, string> AnalysisTypePrompts = new Dictionary<AnalysisType, string>()
    {
        [AnalysisType.Bugs] = L.T(StringKey.AnalyzeBugsPrompt),
        [AnalysisType.Optimize] = L.T(StringKey.AnalyzeOptimizePrompt),
        [AnalysisType.Security] = L.T(StringKey.AnalyzeSecurityPrompt),
        [AnalysisType.General] = L.T(StringKey.AnalyzeGeneralPrompt)
    }.ToFrozenDictionary();

    /// <summary>
    /// 根据分析类型枚举获取对应的提示词
    /// </summary>
    public static string GetAnalysisPrompt(AnalysisType analysisType)
    {
        return AnalysisTypePrompts.GetValueOrDefault(analysisType, AnalysisTypePrompts[AnalysisType.General]);
    }

    /// <summary>
    /// 根据分析类型字符串获取对应的提示词（向后兼容）
    /// </summary>
    public static string GetAnalysisPrompt(string analysisType)
    {
        var enumValue = AnalysisTypeExtensions.FromValue(analysisType) ?? AnalysisType.General;
        return GetAnalysisPrompt(enumValue);
    }

    public static string WorkflowExecute(string task) => L.T(StringKey.WorkflowExecutePrompt, task);

    public static string PlanCreateAndExecute(string prompt) => L.T(StringKey.PlanCreateAndExecutePrompt, prompt);

    public static string GenerateCode(string requirement) => L.T(StringKey.GenerateCodePrompt, requirement);

    public static string AnalyzeCode(string analysisType, string analysisPrompt, string code)
        => L.T(StringKey.AnalyzeCodePrompt, analysisType, analysisPrompt, code);

    public static string Chat(string message) => L.T(StringKey.ChatPrompt, message);
}
