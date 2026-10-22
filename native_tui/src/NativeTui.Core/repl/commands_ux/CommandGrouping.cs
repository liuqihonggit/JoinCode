namespace NativeTui.Core.Repl;

/// <summary>命令分组类别 — 空查询分组视图的组标签（recently-used + 来源组）。</summary>
public enum CommandGroupKind : byte
{
    /// <summary>最近使用（使用计数降序 top5 — 基准 recently used skills）。</summary>
    RecentlyUsed = 0,

    /// <summary>内置命令组。</summary>
    Builtin = 1,

    /// <summary>用户命令组。</summary>
    User = 2,

    /// <summary>项目命令组。</summary>
    Project = 3,

    /// <summary>策略命令组。</summary>
    Policy = 4,

    /// <summary>其他来源兜底组。</summary>
    Other = 5,
}

/// <summary>命令分组类别信息 — 组头中文标题翻译点（SuggestionBox 渲染分组头消费面）。</summary>
public static class CommandGroupKindInfo
{
    /// <summary>组别中文标题（recently-used = 最近使用）。</summary>
    public static string Title(CommandGroupKind kind) => kind switch
    {
        CommandGroupKind.RecentlyUsed => "最近使用",
        CommandGroupKind.Builtin => "内置命令",
        CommandGroupKind.User => "用户命令",
        CommandGroupKind.Project => "项目命令",
        CommandGroupKind.Policy => "策略命令",
        _ => "其他命令",
    };
}

/// <summary>命令分组 — 空查询全量清单的分组视图单元。</summary>
/// <param name="Kind">组类别。</param>
/// <param name="Items">组内命令（字母序）。</param>
public sealed record CommandGroup(CommandGroupKind Kind, IReadOnlyList<CommandSuggestion> Items);

/// <summary>
/// 命令分组投影 — 对齐基准 commandSuggestions.ts 空查询分支（无查询全量清单的分组视图）：
/// recently-used top5（使用计数降序）→ builtin → user → project → policy → other，
/// 各来源组内字母序；recently-used 已含命令不再进来源组（去重）。
/// 两套排序并存：有查询走 SlashSuggester 既有 6 级匹配排序，空查询走本分组投影。
/// </summary>
public sealed class CommandGrouping(CommandUsageMemory usage)
{
    /// <summary>最近使用组条数上限（基准 top 5 recently used skills）。</summary>
    public const int RecentLimit = 5;

    /// <summary>投影为扁平有序列表（recently-used → 来源组拼接 — 直接作 SlashSuggester 空查询 Items）。</summary>
    public IReadOnlyList<CommandSuggestion> Project(IReadOnlyList<CommandSuggestion> commands) =>
        ProjectGrouped(commands).SelectMany(group => group.Items).ToList();

    /// <summary>投影为分组序列（组头渲染消费面 — 空组不输出，recently-used 空则整个组省略）。</summary>
    public IReadOnlyList<CommandGroup> ProjectGrouped(IReadOnlyList<CommandSuggestion> commands)
    {
        var visible = commands.Where(command => !command.IsHidden).ToList();
        var recent = RecentCommands(visible);
        var recentNames = recent.Select(command => command.Name).ToHashSet(StringComparer.Ordinal);
        var sourceGroups = visible
            .Where(command => !recentNames.Contains(command.Name))
            .GroupBy(command => command.Source)
            .Select(group => new CommandGroup(
                ToGroupKind(group.Key),
                group.OrderBy(command => command.Name, StringComparer.Ordinal).ToList()))
            .OrderBy(group => (int)group.Kind)
            .ToList();
        return recent.Count > 0
            ? [new CommandGroup(CommandGroupKind.RecentlyUsed, recent), .. sourceGroups]
            : sourceGroups;
    }

    /// <summary>最近使用命令 — 使用计数降序 → 最近使用时间降序 → 名字字母序，取 top RecentLimit。</summary>
    public IReadOnlyList<CommandSuggestion> RecentCommands(IReadOnlyList<CommandSuggestion> commands) =>
        commands.Where(command => !command.IsHidden)
            .Select(command => (Command: command, Usage: usage.TryGetUsage(command.Name)))
            .Where(pair => pair.Usage is not null)
            .OrderByDescending(pair => pair.Usage!.Count)
            .ThenByDescending(pair => pair.Usage!.LastUsed)
            .ThenBy(pair => pair.Command.Name, StringComparer.Ordinal)
            .Take(RecentLimit)
            .Select(pair => pair.Command)
            .ToList();

    /// <summary>来源枚举 → 组类别（CommandSource 与 CommandGroupKind 的唯一翻译点）。</summary>
    private static CommandGroupKind ToGroupKind(CommandSource source) => source switch
    {
        CommandSource.Builtin => CommandGroupKind.Builtin,
        CommandSource.User => CommandGroupKind.User,
        CommandSource.Project => CommandGroupKind.Project,
        CommandSource.Policy => CommandGroupKind.Policy,
        _ => CommandGroupKind.Other,
    };
}
