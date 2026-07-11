namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// Grep工具提示词
/// </summary>
[ToolPrompt(ToolName = "Grep", Category = ToolPromptCategory.Search)]
public static class GrepToolPrompt
{
    public static string GetDescription() => """
        基于ripgrep的强大搜索工具。

        使用方法：
        - 始终使用Grep工具进行搜索任务。永远不要将grep或rg作为Bash命令调用。Grep工具已针对正确权限和访问进行了优化
        - 支持完整正则表达式语法（如 "log.*Error", "function\\s+\\w+"）
        - 使用glob参数过滤文件（如 "*.js", "**/*.tsx"）或type参数（如 "js", "py", "rust"）
        - 输出模式："content"显示匹配行，"files_with_matches"仅显示文件路径（默认），"count"显示匹配计数
        - 使用Agent工具进行需要多轮搜索的开放式搜索
        - 模式语法：使用ripgrep（不是grep）——字面花括号需要转义（使用 `interface\\{\\}` 在Go代码中查找 `interface{}`）
        - 多行匹配：默认模式下模式只在单行内匹配。对于跨行模式如 `struct \\{[\\s\\S]*?field`，使用 `multiline: true`
        """;
}
