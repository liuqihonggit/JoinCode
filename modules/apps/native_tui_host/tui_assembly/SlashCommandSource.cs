namespace NativeTui.Host;

/// <summary>
/// 斜杠命令清单源 — 目录元数据双源合一：
/// ①元数据基座 = 静态生成目录（DI 注册 ISlashCommandCatalog 时解析；缺席兜底直接实例化
/// GeneratedSlashCommandCatalog — 引擎生成器在本宿主经 IVT 可见的编译期产物，CLI SlashCommandExecutors 先例）；
/// ②运行时门控 = 引擎 DI 的 ChatCommandRegistry（F5 — IChatCommand.IsEnabled 是运行时属性[entitlement 门控]，
/// 静态特性字面量只反映编译期默认值；registry 可解析时按运行时实例过滤命令进 TUI 建议列表，
/// 缺席/命令未注册时保持静态目录全量降级）。
/// 转为 NativeTui.Core 零依赖的 CommandSuggestion 纯数据；
/// 参数补全元数据全部来自 [ChatCommand]/[ChatCommandArg] 特性（ArgumentHint 提示 + Arguments 枚举值域，
/// 生成器提取注入 — 唯一数据源，消除 TUI/GUI/引擎三方各自硬编码）。
/// </summary>
public static class SlashCommandSource
{
    /// <summary>加载命令清单（name 去 / 前缀）— 无目录时返回空列表（TUI 无补全能力降级）。</summary>
    public static IReadOnlyList<CommandSuggestion> Load(IServiceProvider services)
    {
        var catalog = services.GetService<ISlashCommandCatalog>() ?? new GeneratedSlashCommandCatalog();
        var runtimeCommands = services.GetService<ChatCommandRegistry>()?.GetAllCommands();
        return catalog.ByCategory.Values
            .SelectMany(category => category)
            .Where(command => IsEnabledAtRuntime(command, runtimeCommands))
            .Select(ToSuggestion)
            .ToList();
    }

    /// <summary>运行时启用判定 — registry 缺席或命令未注册时降级静态 IsEnabled（编译期默认值）；
    /// registry 有实例时以运行时 IsEnabled 为准（entitlement 门控真值）。
    /// 注：经 GetAllCommands 全量字典（含禁用实例）匹配 —— GetCommand/HasCommand 对禁用命令
    /// 一律 null/false（CategorizedRegistry isEnabled 天然门控），无法区分"禁用"与"未注册"。</summary>
    private static bool IsEnabledAtRuntime(SlashCommandMetadata command, IReadOnlyDictionary<string, IChatCommand>? runtimeCommands)
    {
        var commandName = command.Name.TrimStart('/');
        var match = runtimeCommands?.FirstOrDefault(kvp => kvp.Key.Equals(commandName, StringComparison.OrdinalIgnoreCase));
        return match is { Key: not null, Value: { } runtime } ? runtime.IsEnabled : command.IsEnabled;
    }

    /// <summary>元数据 → 补全建议项（name 去 / 前缀 + 参数元数据透传）。</summary>
    private static CommandSuggestion ToSuggestion(SlashCommandMetadata command)
    {
        var name = command.Name.StartsWith('/') ? command.Name[1..] : command.Name;
        return new CommandSuggestion(
            name,
            command.Description,
            command.Aliases,
            command.IsHidden,
            ArgumentHint: command.ArgumentHint,
            Arguments: command.Arguments);
    }
}
