
namespace Core.Hooks.Configuration;

/// <summary>
/// 钩子匹配器
/// </summary>
public sealed record HookMatcher {
    /// <summary>
    /// 匹配器值（如工具名、通知类型等）
    /// </summary>
    public string? Matcher { get; init; }

    /// <summary>
    /// 钩子列表
    /// </summary>
    public required List<HookCommand> Hooks { get; init; }

    /// <summary>
    /// 技能根目录（技能级钩子用）
    /// </summary>
    public string? SkillRoot { get; init; }

    /// <summary>
    /// 创建匹配器
    /// </summary>
    public static HookMatcher Create(string? matcher, params HookCommand[] hooks) {
        return new HookMatcher {
            Matcher = matcher,
            Hooks = hooks.ToList()
        };
    }

    /// <summary>
    /// 创建适用于所有情况的匹配器
    /// </summary>
    public static HookMatcher CreateForAll(params HookCommand[] hooks) {
        return new HookMatcher {
            Matcher = null,
            Hooks = hooks.ToList()
        };
    }
}

/// <summary>
/// 带来源的钩子配置
/// </summary>
public sealed record SourcedHookConfig {
    /// <summary>
    /// 钩子事件
    /// </summary>
    public required HookEvent Event { get; init; }

    /// <summary>
    /// 钩子命令
    /// </summary>
    public required HookCommand Command { get; init; }

    /// <summary>
    /// 匹配器值
    /// </summary>
    public string? Matcher { get; init; }

    /// <summary>
    /// 配置来源
    /// </summary>
    public required HookSource Source { get; init; }

    /// <summary>
    /// 插件名称（如果来自插件）
    /// </summary>
    public string? PluginName { get; init; }

    /// <summary>
    /// 技能根目录（如果来自技能）
    /// </summary>
    public string? SkillRoot { get; init; }

    /// <summary>
    /// 获取显示文本
    /// </summary>
    public string GetDisplayText() {
        return $"[{Source.GetInlineDisplay()}] {Command.GetDisplayText()}";
    }
}

/// <summary>
/// 钩子配置分组
/// </summary>
public sealed class HookConfigurationGroup {
    private ImmutableHamT<HookEvent, ImmutableHamT<string, ImmutableList<SourcedHookConfig>>> _groups
        = ImmutableHamT<HookEvent, ImmutableHamT<string, ImmutableList<SourcedHookConfig>>>.Empty;

    /// <summary>
    /// 按事件和匹配器分组的钩子
    /// </summary>
    public ImmutableHamT<HookEvent, ImmutableHamT<string, ImmutableList<SourcedHookConfig>>> Groups => _groups;

    /// <summary>
    /// 添加钩子配置
    /// </summary>
    public void Add(SourcedHookConfig config) {
        var matcherKey = config.Matcher ?? "";
        while (true) {
            var current = _groups;
            if (!current.TryGetValue(config.Event, out var eventGroup)) {
                eventGroup = ImmutableHamT<string, ImmutableList<SourcedHookConfig>>.Empty;
            }
            if (!eventGroup.TryGetValue(matcherKey, out var hookList)) {
                hookList = [];
            }
            var newEventGroup = eventGroup.SetItem(matcherKey, hookList.Add(config));
            var updated = current.SetItem(config.Event, newEventGroup);
            if (Interlocked.CompareExchange(ref _groups, updated, current) == current) return;
        }
    }

    /// <summary>
    /// 获取事件的匹配器列表的快照拷贝
    /// </summary>
    public string[] GetMatchers(HookEvent hookEvent) {
        if (!_groups.TryGetValue(hookEvent, out var eventGroup)) {
            return Array.Empty<string>();
        }

        return eventGroup.Keys.ToArray();
    }

    /// <summary>
    /// 获取特定事件和匹配器的钩子
    /// </summary>
    public IReadOnlyList<SourcedHookConfig> GetHooks(HookEvent hookEvent, string? matcher) {
        var matcherKey = matcher ?? "";

        if (!_groups.TryGetValue(hookEvent, out var eventGroup)) {
            return Array.Empty<SourcedHookConfig>();
        }

        if (!eventGroup.TryGetValue(matcherKey, out var hookList)) {
            return Array.Empty<SourcedHookConfig>();
        }

        return hookList;
    }

    /// <summary>
    /// 获取特定事件的所有钩子
    /// </summary>
    public IReadOnlyList<SourcedHookConfig> GetAllHooksForEvent(HookEvent hookEvent) {
        if (!_groups.TryGetValue(hookEvent, out var eventGroup)) {
            return Array.Empty<SourcedHookConfig>();
        }

        return eventGroup.Values.SelectMany(h => h).ToList();
    }

    /// <summary>
    /// 获取排序后的匹配器（按来源优先级）
    /// </summary>
    public IReadOnlyList<string> GetSortedMatchers(HookEvent hookEvent) {
        var matchers = GetMatchers(hookEvent);

        return matchers
            .OrderBy(m => GetMatcherPriority(hookEvent, m))
            .ThenBy(m => m)
            .ToList();
    }

    private int GetMatcherPriority(HookEvent hookEvent, string matcher) {
        var hooks = GetHooks(hookEvent, matcher);
        var sources = hooks.Select(h => h.Source).Distinct();

        return sources.Min(s => s.GetPriority());
    }
}