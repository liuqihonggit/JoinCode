namespace NativeTui.Host;

/// <summary>
/// 斜杠命令清单源 — 从引擎 DI 解析 ISlashCommandCatalog（源码生成器在 Composition 中生成），
/// 未注册时兜底直接实例化 GeneratedSlashCommandCatalog（CLI SlashCommandExecutors 先例）；
/// 过滤 IsEnabled=false，转为 NativeTui.Core 零依赖的 CommandSuggestion 纯数据；
/// 参数补全元数据：ArgumentHint 透传 [ChatCommand] 特性，静态参数清单与 GUI CommandArgumentProvider 同源
/// （/theme /effort /config 三组静态值；/model /provider 动态清单 TUI 侧走 Hint 提示）。
/// </summary>
public static class SlashCommandSource
{
    /// <summary>静态参数值清单（与 GUI CommandArgumentProvider 同值 — 跨端共享提升到 abstractions 是后续重构项）。</summary>
    private static readonly IReadOnlyDictionary<string, string[]> ArgumentValues = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["theme"] = ["dark", "light", "auto"],
        ["effort"] = ["auto", "low", "medium", "high", "max"],
        ["config"] = ["get", "set", "list", "remove"],
    };

    /// <summary>加载命令清单（name 去 / 前缀）— 无目录时返回空列表（TUI 无补全能力降级）。</summary>
    public static IReadOnlyList<CommandSuggestion> Load(IServiceProvider services)
    {
        var catalog = services.GetService<ISlashCommandCatalog>() ?? new GeneratedSlashCommandCatalog();
        return catalog.ByCategory.Values
            .SelectMany(category => category)
            .Where(command => command.IsEnabled)
            .Select(ToSuggestion)
            .ToList();
    }

    /// <summary>元数据 → 补全建议项（name 去 / 前缀 + 参数元数据注入）。</summary>
    private static CommandSuggestion ToSuggestion(SlashCommandMetadata command)
    {
        var name = command.Name.StartsWith('/') ? command.Name[1..] : command.Name;
        ArgumentValues.TryGetValue(name, out var arguments);
        return new CommandSuggestion(
            name,
            command.Description,
            command.Aliases,
            command.IsHidden,
            ArgumentHint: command.ArgumentHint ?? string.Empty,
            Arguments: arguments);
    }
}
