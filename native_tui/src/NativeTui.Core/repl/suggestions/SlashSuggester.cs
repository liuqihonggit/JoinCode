namespace NativeTui.Core.Repl;

/// <summary>
/// 斜杠命令补全状态机 — 对齐基准 commandSuggestions.ts + useTypeahead.tsx 的命令建议分支：
/// 触发（/ 开头、无空格、非 Esc 抑制态）；空查询全量可见命令（默认字母序，可选注入
/// CommandGrouping 走分组视图：recently-used top5 → builtin → user → project → policy）；
/// 二级排序（精确名 &gt; 精确切名 &gt; 前缀名短优先 &gt; 前缀切名 &gt; 子序列名 &gt; 子序列切名）；
/// 隐藏命令输入全名时前插；Dismiss 抑制同一输入，输入变化解除；
/// 选中索引循环导航（对齐基准 autocomplete previous/next 回绕）。
/// 两套排序并存：有查询走既有匹配排序，空查询在注入分组投影时走分组（未注入维持字母序零破坏）。
/// </summary>
public sealed class SlashSuggester
{
    private readonly IReadOnlyList<CommandSuggestion> _commands;
    private readonly CommandGrouping? _grouping;
    private string _currentInput = string.Empty;
    private string _suppressedInput = string.Empty;

    /// <summary>当前建议列表（已过滤排序）。</summary>
    public IReadOnlyList<CommandSuggestion> Items { get; private set; } = [];

    /// <summary>选中索引（默认 0 = 首项预选，基准 commandItems 首项预选语义）。</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>激活态（有建议项）。</summary>
    public bool IsActive => Items.Count > 0;

    /// <summary>选中项（无建议时 null）。</summary>
    public CommandSuggestion? Selected => Items.Count > 0 ? Items[SelectedIndex] : null;

    /// <summary>参数补全模式（"/cmd 参数前缀" 态 — 建议项为参数值或提示，渲染不带 / 前缀）。</summary>
    public bool IsArgumentMode { get; private set; }

    /// <summary>参数模式建议是真实值清单（可 Tab 补全/Enter 填入）— false 表示仅 ArgumentHint 占位提示
    ///（如 "&lt;question&gt;" — Tab/Enter 不得当参数值填入，修复：占位符被补全提交致 `/btw &lt;question&gt;` 字面量发给 AI）。</summary>
    public bool HasRealArgumentValues
    {
        get
        {
            if (!IsArgumentMode || Items.Count == 0)
            {
                return false;
            }

            var commandName = CurrentArgumentCommandName();
            return commandName is not null
                && _commands.FirstOrDefault(c => c.Name == commandName) is { ArgumentValues.Length: > 0 };
        }
    }

    /// <summary>幽灵文本后缀（基准 ghost text — 输入中途 " /前缀" token 的命令补全剩余部分，如 "act"；
    /// 激活时下拉互斥为空；空串 = 未激活）。</summary>
    public string GhostSuffix { get; private set; } = string.Empty;

    /// <summary>命令列宽 — 全量命令最长名 + 6（含 / 与列间距，基准预计算防过滤时布局跳动）。</summary>
    public int NameColumnWidth { get; }

    /// <summary>创建补全状态机 — commands 为宿主注入的全量命令清单；grouping 为空查询分组投影
    ///（可选注入 — null 维持既有字母序零破坏；注入后 "/" 空查询走分组视图，有查询仍走匹配排序）。</summary>
    public SlashSuggester(IReadOnlyList<CommandSuggestion> commands, CommandGrouping? grouping = null)
    {
        _commands = commands;
        _grouping = grouping;
        NameColumnWidth = commands.Count > 0 ? commands.Max(c => c.Name.Length) + 6 : 0;
    }

    /// <summary>当前参数模式命令名（非参数模式或无空格时 null）。</summary>
    private string? CurrentArgumentCommandName()
    {
        var input = _currentInput;
        if (input.Length == 0 || input[0] != '/')
        {
            return null;
        }

        var spaceIndex = input.IndexOf(' ');
        return spaceIndex > 1 ? input[1..spaceIndex] : null;
    }

    /// <summary>输入变化时刷新建议（用户按键编辑路径调用；程序化填充如历史导航不调用）。</summary>
    public void Update(string input)
    {
        _currentInput = input;
        if (input == _suppressedInput)
        {
            (Items, SelectedIndex, IsArgumentMode, GhostSuffix) = ([], 0, false, string.Empty);
            return;
        }

        _suppressedInput = string.Empty;
        var items = Match(input);
        GhostSuffix = items.Count == 0 && input.Length > 0 && input[0] != '/' ? MatchGhostSuffix(input) : string.Empty;
        (Items, SelectedIndex, IsArgumentMode) = (items, 0, IsArgumentInput(input));
    }

    /// <summary>Esc 关闭建议并抑制同一输入（基准 autocomplete:dismiss — dismissedForInputRef，ghost 与下拉共用抑制）。</summary>
    public void Dismiss()
    {
        _suppressedInput = _currentInput;
        (Items, SelectedIndex, IsArgumentMode, GhostSuffix) = ([], 0, false, string.Empty);
    }

    /// <summary>选中索引循环移动（正 = 向下，负 = 向上；越界回绕，基准循环语义）。</summary>
    public void Move(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        SelectedIndex = ((SelectedIndex + delta) % Items.Count + Items.Count) % Items.Count;
    }

    /// <summary>幽灵后缀计算（基准 getBestCommandMatch）— 取最后一个空白后 "/" 起始的尾部 token 查询，
    /// 找第一个前缀匹配命令名的剩余部分；无 token/空前缀/无匹配返回空串。</summary>
    private string MatchGhostSuffix(string input)
    {
        var queryStart = -1;
        for (var i = input.Length - 1; i >= 0; i--)
        {
            var ch = input[i];
            if (ch == '/' && (i == 0 || char.IsWhiteSpace(input[i - 1])))
            {
                queryStart = i + 1;
                break;
            }

            if (char.IsWhiteSpace(ch))
            {
                break;
            }
        }

        if (queryStart <= 0 || queryStart >= input.Length)
        {
            return string.Empty;
        }

        var query = input[queryStart..];
        var command = _commands.FirstOrDefault(c =>
            !c.IsHidden && c.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) && c.Name.Length > query.Length);
        return command is null ? string.Empty : command.Name[query.Length..];
    }

    /// <summary>匹配与排序 — 命令模式（"/前缀"）或参数模式（"/cmd 参数前缀"）；
    /// 不触发（非 / 开头/第二空格/未知命令/无参数元数据）时为空。</summary>
    private List<CommandSuggestion> Match(string input)
    {
        if (input.Length == 0 || input[0] != '/')
        {
            return [];
        }

        var firstSpace = input.IndexOf(' ');
        return firstSpace < 0 ? MatchCommand(input[1..]) : MatchArgument(input, firstSpace);
    }

    /// <summary>参数模式输入判定（供 IsArgumentMode 同步）。</summary>
    private bool IsArgumentInput(string input)
    {
        var firstSpace = input.IndexOf(' ');
        return firstSpace >= 0 && MatchArgument(input, firstSpace).Count > 0;
    }

    /// <summary>命令名模式匹配（无空格 — 二级排序链；空查询注入分组投影时走分组视图）。</summary>
    private List<CommandSuggestion> MatchCommand(string query)
    {
        if (query.Length == 0)
        {
            var visible = _commands.Where(c => !c.IsHidden).ToList();
            return _grouping is not null
                ? _grouping.Project(visible).ToList()
                : visible.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        }

        var ranked = _commands.Where(c => !c.IsHidden)
            .Select(c => (Command: c, Tier: MatchTier(c, query)))
            .Where(pair => pair.Tier >= 0)
            .OrderBy(pair => pair.Tier)
            .ThenBy(pair => pair.Command.Name.Length)
            .ThenBy(pair => pair.Command.Name, StringComparer.Ordinal)
            .Select(pair => pair.Command)
            .ToList();

        var hiddenExact = _commands.Where(c => c.IsHidden && MatchesExactly(c, query)).ToList();
        foreach (var hidden in hiddenExact)
        {
            ranked.Insert(0, hidden);
        }

        return ranked;
    }

    /// <summary>参数模式匹配 — "/cmd 参数前缀"（恰好一个空格；第二空格不触发）：
    /// 有静态参数清单 → 前缀过滤参数值项；仅 ArgumentHint → 单条提示项；均无 → 不触发。</summary>
    private List<CommandSuggestion> MatchArgument(string input, int firstSpace)
    {
        var commandName = input[1..firstSpace];
        var argumentPrefix = input[(firstSpace + 1)..];
        if (argumentPrefix.Contains(' '))
        {
            return [];
        }

        var command = _commands.FirstOrDefault(c => c.Name == commandName);
        if (command is null)
        {
            return [];
        }

        if (command.ArgumentValues.Length > 0)
        {
            return command.ArgumentValues
                .Where(a => a.StartsWith(argumentPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(a => new CommandSuggestion(a, command.Description, [], IsHidden: false))
                .ToList();
        }

        return command.ArgumentHint.Length > 0
            ? [new CommandSuggestion(command.ArgumentHint, command.Description, [], IsHidden: false)]
            : [];
    }

    /// <summary>精确匹配（名或切名等于查询）。</summary>
    private static bool MatchesExactly(CommandSuggestion command, string query) =>
        command.Name == query || command.Aliases.Contains(query, StringComparer.OrdinalIgnoreCase);

    /// <summary>匹配层级 — -1 = 不匹配；0 = 精确名；1 = 精确切名；2 = 前缀名；3 = 前缀切名；4 = 子序列名；5 = 子序列切名。</summary>
    private static int MatchTier(CommandSuggestion command, string query)
    {
        if (command.Name == query)
        {
            return 0;
        }

        if (command.Aliases.Contains(query, StringComparer.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (command.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (command.Aliases.Any(a => a.StartsWith(query, StringComparison.OrdinalIgnoreCase)))
        {
            return 3;
        }

        if (IsSubsequence(query, command.Name))
        {
            return 4;
        }

        if (command.Aliases.Any(a => IsSubsequence(query, a)))
        {
            return 5;
        }

        return -1;
    }

    /// <summary>子序列匹配（Fuse 简化）：query 各字符按顺序出现于 target（大小写不敏感）。</summary>
    private static bool IsSubsequence(string query, string target)
    {
        var matched = 0;
        foreach (var ch in target)
        {
            if (matched < query.Length && char.ToLowerInvariant(ch) == char.ToLowerInvariant(query[matched]))
            {
                matched++;
            }
        }

        return matched == query.Length;
    }
}
