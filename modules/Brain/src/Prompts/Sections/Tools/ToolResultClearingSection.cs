using JoinCode.Abstractions.Attributes;
using Core.Prompts;

namespace Core.Prompts.Sections;

/// <summary>
/// 工具结果清除部分 - 关于工具结果自动清除的说明
/// </summary>
[PromptSection(Name = "tool_result_clearing", Order = 19)]
public static class ToolResultClearingSection
{
    public static string? GetContent()
    {
        return $"""
# 工具结果清除

旧的工具结果将自动从上下文中清除以释放空间。始终保留最近的5个结果。

使用工具结果时，在回复中写下任何您稍后可能需要的重要信息，因为原始工具结果可能会被稍后清除。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("tool_result_clearing", GetContent);
}
