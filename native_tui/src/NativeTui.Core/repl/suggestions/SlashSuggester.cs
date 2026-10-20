namespace NativeTui.Core.Repl;

/// <summary>
/// 斜杠命令补全状态机 — 对齐基准 commandSuggestions.ts + useTypeahead.tsx 的命令建议分支：
/// 触发（/ 开头、无空格、非 Esc 抑制态）；空查询全量可见命令字母序；
/// 二级排序（精确名 &gt; 精确切名 &gt; 前缀名短优先 &gt; 前缀切名 &gt; 子序列名 &gt; 子序列切名）；
/// 隐藏命令输入全名时前插；Dismiss 抑制同一输入，输入变化解除；
/// 选中索引循环导航（对齐基准 autocomplete previous/next 回绕）。
/// </summary>
public sealed class SlashSuggester
{
    private readonly IReadOnlyList<CommandSuggestion> _commands;
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

    /// <summary>命令列宽 — 全量命令最长名 + 6（含 / 与列间距，基准预计算防过滤时布局跳动）。</summary>
    public int NameColumnWidth { get; }

    /// <summary>创建补全状态机 — commands 为宿主注入的全量命令清单。</summary>
    public SlashSuggester(IReadOnlyList<CommandSuggestion> commands)
    {
        _commands = commands;
        NameColumnWidth = commands.Count > 0 ? commands.Max(c => c.Name.Length) + 6 : 0;
    }

    /// <summary>输入变化时刷新建议（用户按键编辑路径调用；程序化填充如历史导航不调用）。</summary>
    public void Update(string input)
    {
        _currentInput = input;
        if (input == _suppressedInput)
        {
            Items = [];
            SelectedIndex = 0;
            return;
        }

        _suppressedInput = string.Empty;
        (Items, SelectedIndex) = (Match(input), 0);
    }

    /// <summary>Esc 关闭建议并抑制同一输入（基准 autocomplete:dismiss — dismissedForInputRef）。</summary>
    public void Dismiss()
    {
        _suppressedInput = _currentInput;
        Items = [];
        SelectedIndex = 0;
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

    /// <summary>匹配与排序 — 返回新建议列表；不触发（非 / 开头/含空格/空输入）时为空。</summary>
    private List<CommandSuggestion> Match(string input)
    {
        if (input.Length == 0 || input[0] != '/' || input.Contains(' '))
        {
            return [];
        }

        var query = input[1..];
        if (query.Length == 0)
        {
            return _commands.Where(c => !c.IsHidden)
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .ToList();
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
