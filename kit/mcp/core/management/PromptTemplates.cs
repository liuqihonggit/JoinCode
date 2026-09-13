

namespace McpToolDispatch;

/// <summary>
/// MCP 工作流提示词模板集合 — 工作流操作的本地化提示词构建
/// </summary>
[PromptTemplate(Name = "mcp_workflow", Category = PromptTemplateCategory.Mcp, Description = "MCP工作流提示词模板集合（执行/计划/代码生成/代码分析/对话）", HasParameters = true)]
public static class PromptTemplates
{
    /// <summary>
    /// 分析类型提示词字典 — 以 AnalysisType 枚举为键，提供只读高效访问
    /// </summary>
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

    /// <summary>
    /// 构建工作流执行提示词
    /// </summary>
    /// <param name="task">任务描述</param>
    /// <returns>格式化后的工作流执行提示词</returns>
    public static string WorkflowExecute(string task) => L.T(StringKey.WorkflowExecutePrompt, task);

    /// <summary>
    /// 构建计划创建并执行提示词
    /// </summary>
    /// <param name="prompt">用户提示</param>
    /// <returns>格式化后的计划创建并执行提示词</returns>
    public static string PlanCreateAndExecute(string prompt) => L.T(StringKey.PlanCreateAndExecutePrompt, prompt);

    /// <summary>
    /// 构建代码生成提示词
    /// </summary>
    /// <param name="requirement">需求描述</param>
    /// <returns>格式化后的代码生成提示词</returns>
    public static string GenerateCode(string requirement) => L.T(StringKey.GenerateCodePrompt, requirement);

    /// <summary>
    /// 构建代码分析提示词
    /// </summary>
    /// <param name="analysisType">分析类型</param>
    /// <param name="analysisPrompt">分析提示</param>
    /// <param name="code">待分析代码</param>
    /// <returns>格式化后的代码分析提示词</returns>
    public static string AnalyzeCode(string analysisType, string analysisPrompt, string code)
        => L.T(StringKey.AnalyzeCodePrompt, analysisType, analysisPrompt, code);

    /// <summary>
    /// 构建对话提示词
    /// </summary>
    /// <param name="message">用户消息</param>
    /// <returns>格式化后的对话提示词</returns>
    public static string Chat(string message) => L.T(StringKey.ChatPrompt, message);
}
