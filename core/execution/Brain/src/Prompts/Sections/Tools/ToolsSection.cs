using JoinCode.Abstractions.Attributes;
using Core.Prompts;

namespace Core.Prompts.Sections;

/// <summary>
/// 工具使用部分 - 如何使用工具
/// </summary>
[PromptSection(Name = "tools", Order = 11)]
public static class ToolsSection {
    public static string? GetContent() {
        var enabledTools = PromptConfigSnapshot.Current.EnabledTools;
        var tools = enabledTools?.ToList() ?? new List<string>();

        var items = new List<string> {
            "不要滥用Bash工具来运行命令。使用专用工具可以让用户更好地理解和审查您的工作。这对协助用户至关重要："
        };

        var toolGuidance = new List<string> {
            "使用FileReadTool读取文件，而不是cat、head、tail或sed",
            "使用FileEditTool编辑文件，而不是sed或awk",
            "使用FileWriteTool创建文件，而不是使用heredoc或echo重定向的cat",
            "使用GlobTool搜索文件，而不是find或ls",
            "使用GrepTool搜索文件内容，而不是grep或rg",
            "将Bash工具专门用于需要shell执行的系统命令和终端操作。如果您不确定并且有相关的专用工具，请默认使用专用工具，只有在绝对必要时才回退到使用Bash工具。"
        };

        items.AddRange(toolGuidance);
        items.Add("使用TodoWriteTool工具分解和管理工作。这些工具有助于规划工作和帮助用户跟踪您的进度。完成任务后立即标记为完成。不要批量完成多个任务后再标记。");
        items.Add("您可以在单个响应中调用多个工具。如果您打算调用多个工具并且它们之间没有依赖关系，请并行进行所有独立的工具调用。尽可能最大化并行工具调用的使用以提高效率。但是，如果某些工具调用依赖于先前的调用来通知依赖值，请不要并行调用这些工具，而是顺序调用它们。例如，如果一个操作必须在另一个开始之前完成，请顺序运行这些操作。");

        var result = new System.Text.StringBuilder();
        result.AppendLine("# 使用您的工具");
        foreach (var item in items) {
            result.AppendLine($" - {item}");
        }

        return result.ToString().TrimEnd();
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("tools", GetContent);
}
