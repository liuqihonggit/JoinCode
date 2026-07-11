namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// Glob工具提示词
/// </summary>
[ToolPrompt(ToolName = "Glob", Category = ToolPromptCategory.Search)]
public static class GlobToolPrompt
{
    public static string GetDescription() => """
        快速文件模式匹配工具，适用于任何规模的代码库。

        使用方法：
        - 支持glob模式，如 "**/*.js" 或 "src/**/*.ts"
        - 返回匹配的文件路径，按修改时间排序
        - 当您需要按名称模式查找文件时使用此工具
        - 当进行可能需要多轮glob和grep的开放式搜索时，使用Agent工具代替
        """;
}
