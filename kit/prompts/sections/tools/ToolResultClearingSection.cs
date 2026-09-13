namespace Core.Prompts.Sections;

/// <summary>
/// 工具结果清除部分 - 关于工具结果自动清除的说明
/// </summary>
[PromptSection(Name = "tool_result_clearing", Order = 19)]
public static class ToolResultClearingSection
{
    /// <summary>
    /// 获取工具结果清除部分的提示词内容。
    /// </summary>
    /// <returns>工具结果清除说明文本；始终非空。</returns>
    public static string? GetContent()
    {
        return $"""
# 工具结果清除

旧的工具结果将自动从上下文中清除以释放空间。始终保留最近的5个结果。

使用工具结果时，在回复中写下任何您稍后可能需要的重要信息，因为原始工具结果可能会被稍后清除。
""";
    }

    /// <summary>
    /// 创建工具结果清除 Section 实例（内容缓存）。
    /// </summary>
    /// <returns>工具结果清除 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("tool_result_clearing", GetContent);
}
