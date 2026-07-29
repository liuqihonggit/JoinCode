
namespace Core.Prompts.Sections;

/// <summary>
/// REPL模式部分 - 交互式编程环境说明
/// </summary>
[PromptSection(Name = "repl_mode", Order = 27)]
public static class ReplModeSection
{
    public static string? GetContent()
    {
        var isReplMode = PromptConfigSnapshot.Current.IsReplMode;
        if (!isReplMode)
        {
            return null;
        }

        return """
# REPL模式

您正在REPL（交互式编程环境）中运行。在此模式下：

- 可以直接执行代码片段并查看结果
- 支持多行代码输入
- 变量和状态在会话之间保持
- 可以使用特殊的REPL命令

使用Read、Write、Edit、Glob、Grep、Bash、Agent等工具时，请遵循REPL环境的特定用法。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("repl_mode", GetContent);
}
