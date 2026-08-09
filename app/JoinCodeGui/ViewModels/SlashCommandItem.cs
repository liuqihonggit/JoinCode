namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 斜杠命令项 — 命令面板中展示的单条命令元数据。
/// 命令列表对齐 CLI 端常用命令子集，GUI 端仅保留高频命令。
/// </summary>
public sealed class SlashCommandItem
{
    /// <summary>命令名（如 "/clear"）</summary>
    public required string Name { get; init; }

    /// <summary>命令描述（如 "清空聊天历史"）</summary>
    public required string Description { get; init; }

    /// <summary>用法提示（如 "/clear" 或 "/model [model-id]"）</summary>
    public string Usage { get; init; } = string.Empty;

    /// <summary>显示文本（命令名 + 描述，供面板列表渲染）</summary>
    public string DisplayText => $"{Name}  —  {Description}";

    /// <summary>
    /// GUI 端常用斜杠命令列表 — 从 CLI 80+ 命令中选取高频子集。
    /// 后续可改为从 ChatCommandRegistry 动态拉取。
    /// </summary>
    public static readonly IReadOnlyList<SlashCommandItem> BuiltInCommands =
    [
        new() { Name = "/clear",    Description = "清空聊天历史并释放上下文",     Usage = "/clear" },
        new() { Name = "/compact",  Description = "压缩对话上下文以节省 Token",   Usage = "/compact [自定义摘要指令]" },
        new() { Name = "/model",    Description = "切换或查看模型",               Usage = "/model [model-id|default|info]" },
        new() { Name = "/resume",   Description = "恢复之前的会话",               Usage = "/resume [session-id]" },
        new() { Name = "/history",  Description = "查看聊天历史",                 Usage = "/history" },
        new() { Name = "/export",   Description = "导出对话到文件或剪贴板",       Usage = "/export [filename|--clipboard]" },
        new() { Name = "/copy",     Description = "复制最近的 AI 回复到剪贴板",   Usage = "/copy [N|code]" },
        new() { Name = "/diff",     Description = "查看未提交变更和每轮 diff",    Usage = "/diff [files|cached]" },
        new() { Name = "/commit",   Description = "创建 Git 提交",                Usage = "/commit [message]" },
        new() { Name = "/help",     Description = "显示帮助信息",                 Usage = "/help" },
        new() { Name = "/status",   Description = "显示版本、模型、连接状态",     Usage = "/status" },
        new() { Name = "/tools",    Description = "显示可用工具列表",             Usage = "/tools" },
        new() { Name = "/config",   Description = "管理配置设置",                 Usage = "/config [get|set|list|remove]" },
        new() { Name = "/theme",    Description = "切换主题",                     Usage = "/theme [dark|light|auto]" },
        new() { Name = "/doctor",   Description = "诊断环境配置和依赖",           Usage = "/doctor" },
        new() { Name = "/exit",     Description = "退出程序",                     Usage = "/exit" }
    ];

    /// <summary>按输入前缀过滤命令（如 "/c" 匹配 /clear、/compact、/copy、/config）</summary>
    public static IReadOnlyList<SlashCommandItem> Filter(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return BuiltInCommands;
        return BuiltInCommands
            .Where(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
