namespace NativeTui.Host;

/// <summary>
/// 斜杠命令清单源 — 从引擎 DI 解析 ISlashCommandCatalog（源码生成器在 Composition 中生成），
/// 未注册时兜底直接实例化 GeneratedSlashCommandCatalog（CLI SlashCommandExecutors 先例）；
/// 过滤 IsEnabled=false，转为 NativeTui.Core 零依赖的 CommandSuggestion 纯数据。
/// </summary>
public static class SlashCommandSource
{
    /// <summary>加载命令清单（name 去 / 前缀）— 无目录时返回空列表（TUI 无补全能力降级）。</summary>
    public static IReadOnlyList<CommandSuggestion> Load(IServiceProvider services)
    {
        var catalog = services.GetService<ISlashCommandCatalog>() ?? new GeneratedSlashCommandCatalog();
        return catalog.ByCategory.Values
            .SelectMany(category => category)
            .Where(command => command.IsEnabled)
            .Select(command => new CommandSuggestion(
                command.Name.StartsWith('/') ? command.Name[1..] : command.Name,
                command.Description,
                command.Aliases,
                command.IsHidden))
            .ToList();
    }
}
