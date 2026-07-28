using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 工具结果总结部分 - 关于工具结果处理的说明
/// </summary>
[PromptSection(Name = "summarize_tool_results", Order = 20)]
public static class SummarizeToolResultsSection {
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("summarize_tool_results", () => {
            return """
# 工具结果总结

处理工具结果时，在回复中写下您稍后可能需要的任何重要信息，因为原始工具结果可能会在之后被清除。
""";
        });
    }
}
