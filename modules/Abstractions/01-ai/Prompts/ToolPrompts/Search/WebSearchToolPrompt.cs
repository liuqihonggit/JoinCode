namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// WebSearch工具提示词
/// </summary>
[ToolPrompt(ToolName = "WebSearch", Category = ToolPromptCategory.Search)]
public static class WebSearchToolPrompt
{
    public static string GetDescription(string currentMonthYear) => $"""
        - 允许AI搜索网络并使用结果来通知回复
        - 为当前事件和最近数据提供最新信息
        - 返回格式化为搜索结果块的搜索结果信息，包括作为markdown超链接的链接
        - 使用此工具访问超出AI知识截止日期的信息
        - 搜索在单个API调用中自动执行

        关键要求 - 您必须遵循此要求：
          - 回答用户问题后，您必须在回复末尾包含一个"来源："部分
          - 在来源部分，列出搜索结果中的所有相关URL作为markdown超链接：[标题](URL)
          - 这是强制性的 - 永远不要跳过在回复中包含来源
          - 示例格式：

            [您的答案在这里]

            来源：
            - [来源标题1](https://example.com/1)
            - [来源标题2](https://example.com/2)

        使用说明：
          - 支持域过滤以包含或阻止特定网站
          - 网络搜索仅在特定区域可用

        重要 - 在搜索查询中使用正确的年份：
          - 当前月份是{currentMonthYear}。在搜索最近信息、文档或当前事件时，您必须使用此年份，而不是去年
          - 示例：如果用户询问"最新的React文档"，请使用当前年份搜索"React documentation"
        """;
}
