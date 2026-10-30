namespace Core.Prompts.Sections;

/// <summary>
/// 工具使用部分 - 如何使用工具
/// </summary>
[PromptSection(Name = "tools", Order = 11)]
public static class ToolsSection {
    /// <summary>
    /// 获取工具使用部分的提示词内容。根据启用的工具列表拼接工具使用指导。
    /// </summary>
    /// <returns>工具使用指导文本。</returns>
    public static string? GetContent() {
        var enabledTools = PromptConfigSnapshot.Current.EnabledTools;
        var tools = enabledTools?.ToList() ?? new List<string>();

        var items = new List<string> {
            $"不要滥用{ShellToolNameEnumConstants.Bash}工具来运行命令。使用专用工具可以让用户更好地理解和审查您的工作。这对协助用户至关重要："
        };

        var toolGuidance = new List<string> {
            $"使用{FileToolNameEnumConstants.FileRead}读取文件，而不是cat、head、tail或sed",
            $"使用{FileToolNameEnumConstants.FileEdit}编辑文件，而不是sed或awk",
            $"使用{FileToolNameEnumConstants.FileWrite}创建文件，而不是使用heredoc或echo重定向的cat",
            $"使用{SearchToolNameEnumConstants.Glob}搜索文件，而不是find或ls",
            $"使用{SearchToolNameEnumConstants.Grep}搜索文件内容，而不是grep或rg",
            $"将{ShellToolNameEnumConstants.Bash}工具专门用于需要shell执行的系统命令和终端操作。如果您不确定并且有相关的专用工具，请默认使用专用工具，只有在绝对必要时才回退到使用{ShellToolNameEnumConstants.Bash}工具。"
        };

        items.AddRange(toolGuidance);
        items.Add($"反向提醒：当专用工具无法覆盖某些场景时（如进程管理、系统配置、管道组合、环境调试等），{ShellToolNameEnumConstants.Bash}和系统工具是正确选择。不要因为训练数据习惯而回避使用{ShellToolNameEnumConstants.Bash}——该用就用，关键是判断\"是否有专用工具能更好地完成这个任务\"。");
        items.Add($"使用{TodoToolNameEnumConstants.TodoWrite}工具分解和管理工作。这些工具有助于规划工作和帮助用户跟踪您的进度。完成任务后立即标记为完成。不要批量完成多个任务后再标记。");
        items.Add("您可以在单个响应中调用多个工具。如果您打算调用多个工具并且它们之间没有依赖关系，请并行进行所有独立的工具调用。尽可能最大化并行工具调用的使用以提高效率。但是，如果某些工具调用依赖于先前的调用来通知依赖值，请不要并行调用这些工具，而是顺序调用它们。例如，如果一个操作必须在另一个开始之前完成，请顺序运行这些操作。");
        items.Add($"MCP 工具采用两阶段加载：上方清单仅含高频核心工具（完整参数定义），其余工具按组别存放、不在首轮清单中 — 遇到下列情况按 → 行动：");
        items.Add($"首轮清单没有需要的能力 → 调用 {SystemToolNameEnumConstants.ToolSearch} 并传 query='list_groups'，列出全部工具组别；");
        items.Add($"看到疑似相关的组别 → query='map[组别名]' 展开该组全部工具名与描述；");
        items.Add($"需要某工具的完整参数定义 → query='map[组别名][工具名]' 直取 schema 后按定义调用；");
        items.Add($"知道工具名但不知道属于哪组 → query='关键词' 模糊搜索，结果自带 [组别[子组]] 路径；");
        items.Add($"若感知不到某个工具，可通过 {SystemToolNameEnumConstants.ToolSearch} 搜索、组别展开（list_groups / map[组别名]）等方式获取工具名称 — 不要因首轮清单未列出就断言工具不存在。");
        items.Add($"工具组规模差异极大 → 按需下钻到目标工具即可，不要整组展开后逐个阅读。");
        items.Add($"若感知不到某个工具，可通过 {SystemToolNameEnumConstants.ToolSearch} 搜索工具、组别展开（list_groups / map[分组]）等方式获取工具名称与完整定义 — 不要因首轮工具清单未列出就断言工具不存在。");

        var result = new System.Text.StringBuilder();
        result.AppendLine("# 使用您的工具");
        foreach (var item in items) {
            result.AppendLine($" - {item}");
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// 创建工具使用 Section 实例（内容缓存）。
    /// </summary>
    /// <returns>工具使用 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("tools", GetContent);
}