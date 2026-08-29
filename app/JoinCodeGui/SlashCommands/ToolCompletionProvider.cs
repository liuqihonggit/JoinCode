namespace JoinCode.Gui.SlashCommands;

/// <summary>
/// 工具补全提供器 — # 触发符调用，提供引擎可用工具列表。
/// 优先使用引擎 IToolRegistry 动态工具列表，引擎未就绪时回退高频工具占位列表。
/// </summary>
public static class ToolCompletionProvider
{
    /// <summary>获取工具补全候选（按前缀过滤；优先引擎真实工具，回退占位列表）</summary>
    public static IReadOnlyList<SlashCommandItem> GetTools(
        string prefix, IReadOnlyList<ToolSummary>? availableTools = null)
    {
        var source = BuildSource(availableTools);
        if (string.IsNullOrEmpty(prefix))
            return source;
        return source
            .Where(t => t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>构建工具源 — 引擎工具非空时用引擎列表，否则回退占位</summary>
    private static IReadOnlyList<SlashCommandItem> BuildSource(IReadOnlyList<ToolSummary>? availableTools)
    {
        if (availableTools is null || availableTools.Count == 0)
            return BuiltInTools;
        var items = new SlashCommandItem[availableTools.Count];
        for (var i = 0; i < availableTools.Count; i++)
        {
            var t = availableTools[i];
            items[i] = new SlashCommandItem { Name = t.Name, Description = t.Description };
        }
        return items;
    }

    /// <summary>高频工具占位列表 — 引擎未就绪时的回退</summary>
    private static readonly IReadOnlyList<SlashCommandItem> BuiltInTools =
    [
        new() { Name = "ReadFile",      Description = "读取文件内容" },
        new() { Name = "WriteFile",     Description = "写入文件" },
        new() { Name = "EditFile",      Description = "编辑文件" },
        new() { Name = "ListFiles",     Description = "列出目录文件" },
        new() { Name = "SearchCode",    Description = "搜索代码" },
        new() { Name = "RunCommand",    Description = "执行命令" },
        new() { Name = "WebSearch",     Description = "网络搜索" },
        new() { Name = "WebFetch",      Description = "获取网页内容" },
        new() { Name = "Grep",          Description = "正则搜索文件内容" },
        new() { Name = "Glob",          Description = "通配符匹配文件" },
        new() { Name = "GitStatus",     Description = "查看 Git 状态" },
        new() { Name = "GitDiff",       Description = "查看 Git 差异" },
        new() { Name = "GitCommit",     Description = "创建 Git 提交" },
        new() { Name = "Task",          Description = "启动子任务" },
        new() { Name = "TodoWrite",     Description = "管理任务清单" }
    ];
}
