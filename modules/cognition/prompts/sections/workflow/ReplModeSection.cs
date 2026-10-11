
namespace Core.Prompts.Sections;

/// <summary>
/// REPL模式部分 - 交互式编程环境说明
/// </summary>
[PromptSection(Name = "repl_mode", Order = 27)]
public static class ReplModeSection {
    /// <summary>
    /// 获取 REPL 模式部分的提示词内容。当未启用 REPL 模式时返回 null。
    /// </summary>
    /// <returns>REPL 模式说明文本；若未启用 REPL 模式则返回 null。</returns>
    public static string? GetContent() {
        var isReplMode = PromptConfigSnapshot.Current.IsReplMode;
        if (!isReplMode) {
            return null;
        }

        return $"""
# REPL模式

您正在REPL（交互式编程环境）中运行。在此模式下：

- 可以直接执行代码片段并查看结果
- 支持多行代码输入
- 变量和状态在会话之间保持
- 可以使用特殊的REPL命令

使用{FileToolNameEnumConstants.FileRead}、{FileToolNameEnumConstants.FileWrite}、{FileToolNameEnumConstants.FileEdit}、{SearchToolNameEnumConstants.Glob}、{SearchToolNameEnumConstants.Grep}、{ShellToolNameEnumConstants.Bash}、{AgentToolNameEnumConstants.Agent}等工具时，请遵循REPL环境的特定用法。
""";
    }

    /// <summary>
    /// 创建 REPL 模式 Section 实例（内容缓存）。
    /// </summary>
    /// <returns>REPL 模式 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("repl_mode", GetContent);
}