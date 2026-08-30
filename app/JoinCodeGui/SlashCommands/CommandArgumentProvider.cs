namespace JoinCode.Gui.SlashCommands;

/// <summary>
/// 命令参数补全提供器 — 为支持参数补全的斜杠命令提供候选列表。
/// /model → 当前供应商可用模型；/theme → 主题；/effort → 推理力度；/config → 子命令；/provider → 供应商。
/// </summary>
public static class CommandArgumentProvider
{
    /// <summary>获取命令参数补全候选（按前缀过滤）</summary>
    public static IReadOnlyList<SlashCommandItem> GetArguments(
        string commandName, string prefix, IJccChatSession session)
    {
        var candidates = commandName.ToLowerInvariant() switch
        {
            "/model" => GetModelArguments(session),
            "/theme" => GetThemeArguments(),
            "/effort" => GetEffortArguments(),
            "/config" => GetConfigArguments(),
            "/provider" => GetProviderArguments(),
            _ => []
        };

        if (string.IsNullOrEmpty(prefix))
            return candidates;

        return candidates
            .Where(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static IReadOnlyList<SlashCommandItem> GetModelArguments(IJccChatSession session)
    {
        var models = session.VendorModelMap.TryGetValue(session.CurrentVendor, out var list) && list is not null
            ? list
            : Array.Empty<string>();
        var items = new SlashCommandItem[models.Count];
        for (var i = 0; i < models.Count; i++)
        {
            var id = models[i];
            items[i] = new SlashCommandItem
            {
                Name = id,
                Description = "切换到模型 " + id
            };
        }
        return items;
    }

    private static IReadOnlyList<SlashCommandItem> GetThemeArguments() =>
    [
        new() { Name = "dark",  Description = "深色主题" },
        new() { Name = "light", Description = "浅色主题" },
        new() { Name = "auto",  Description = "跟随系统" }
    ];

    private static IReadOnlyList<SlashCommandItem> GetEffortArguments() =>
    [
        new() { Name = "auto",   Description = "自动选择（默认）" },
        new() { Name = "low",    Description = "低推理力度，快速响应" },
        new() { Name = "medium", Description = "中等推理力度" },
        new() { Name = "high",   Description = "高推理力度，深度思考" },
        new() { Name = "max",    Description = "最大推理力度" }
    ];

    private static IReadOnlyList<SlashCommandItem> GetConfigArguments() =>
    [
        new() { Name = "get",    Description = "读取配置项" },
        new() { Name = "set",    Description = "设置配置项" },
        new() { Name = "list",   Description = "列出全部配置" },
        new() { Name = "remove", Description = "移除配置项" }
    ];

    private static IReadOnlyList<SlashCommandItem> GetProviderArguments()
    {
        var providers = Enum.GetValues<VendorKind>();
        var items = new SlashCommandItem[providers.Length];
        for (var i = 0; i < providers.Length; i++)
        {
            var value = providers[i].ToValue();
            items[i] = new SlashCommandItem
            {
                Name = value,
                Description = providers[i].ToString() + " 供应商"
            };
        }
        return items;
    }
}
