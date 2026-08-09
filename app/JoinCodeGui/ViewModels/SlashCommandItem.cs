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

    /// <summary>是否展示用法提示（有非空 Usage 时）</summary>
    public bool HasUsage => !string.IsNullOrWhiteSpace(Usage);

    /// <summary>是否启用（禁用命令视为无权限，从候选面板过滤）</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>命令名中匹配当前输入前缀的部分（高亮显示用，由 ViewModel 设置）</summary>
    public string MatchedPart { get; set; } = string.Empty;

    /// <summary>命令名中未匹配前缀的剩余部分（正常显示用，由 ViewModel 设置）</summary>
    public string RemainingPart { get; set; } = string.Empty;

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

    /// <summary>
    /// 从 <see cref="SlashCommandMetadata"/> 列表创建 <see cref="SlashCommandItem"/> 列表。
    /// 由源码生成器从 [ChatCommand] 特性自动提取，替代硬编码 BuiltInCommands。
    /// </summary>
    public static IReadOnlyList<SlashCommandItem> FromMetadata(IReadOnlyList<SlashCommandMetadata> commands)
    {
        return commands
            .Select(c => new SlashCommandItem
            {
                Name = "/" + c.Name,
                Description = c.Description,
                Usage = c.Usage,
                IsEnabled = c.IsEnabled
            })
            .ToList();
    }

    /// <summary>按输入前缀过滤命令（如 "/c" 匹配 /clear、/compact、/copy、/config），并排除禁用命令</summary>
    public static IReadOnlyList<SlashCommandItem> Filter(string prefix, IReadOnlyList<SlashCommandItem>? commands = null)
    {
        var source = commands ?? BuiltInCommands;
        var matched = TrieCache.GetValue(source, list => new SlashCommandTrie(list)).Match(prefix);
        return matched.All(c => c.IsEnabled) ? matched : matched.Where(c => c.IsEnabled).ToList();
    }

    /// <summary>命令列表 → 前缀树缓存（同列表实例复用同一棵树）</summary>
    private static readonly ConditionalWeakTable<IReadOnlyList<SlashCommandItem>, SlashCommandTrie> TrieCache = new();
}
