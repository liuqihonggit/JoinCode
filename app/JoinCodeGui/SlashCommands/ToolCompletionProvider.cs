using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.SlashCommands;

/// <summary>
/// 工具补全提供器 — # 触发符调用，提供引擎可用工具列表。
/// 当前为高频工具占位列表，后续可接入引擎 IToolRegistry 动态获取。
/// </summary>
public static class ToolCompletionProvider
{
    /// <summary>获取工具补全候选（按前缀过滤）</summary>
    public static IReadOnlyList<SlashCommandItem> GetTools(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return BuiltInTools;
        return BuiltInTools
            .Where(t => t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>高频工具占位列表 — 后续接入引擎动态工具注册表</summary>
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
