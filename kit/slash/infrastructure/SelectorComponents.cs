namespace JoinCode.Cli;

// ─── Selector ───

/// <summary>
/// 交互式选择器 — CLI 简化版，使用数字选择替代 TUI 上下键
/// </summary>
public sealed class Selector<T>
{
    private readonly string _title;
    private readonly (T Item, string DisplayText, string SearchKey)[] _items;

    /// <summary>
    /// 构造选择器实例，使用预格式化的项元组数组
    /// </summary>
    /// <param name="title">选择器标题</param>
    /// <param name="items">项元组数组，每项包含原始对象、显示文本、搜索键</param>
    public Selector(string title, (T Item, string DisplayText, string SearchKey)[] items)
    {
        _title = title;
        _items = items;
    }

    /// <summary>
    /// 构造选择器实例，使用显示选择器和可选搜索选择器从原始项数组生成项元组
    /// </summary>
    /// <param name="title">选择器标题</param>
    /// <param name="items">原始项数组</param>
    /// <param name="displaySelector">将项转换为显示文本的委托</param>
    /// <param name="searchSelector">将项转换为搜索键的可选委托，缺省时使用显示文本</param>
    /// <param name="enableSearch">是否启用搜索功能（保留参数，CLI 简化版未实现）</param>
    public Selector(string title, T[] items, Func<T, string> displaySelector, Func<T, string>? searchSelector = null, bool enableSearch = false)
    {
        _title = title;
        _items = items.Select(i => (i, displaySelector(i), searchSelector?.Invoke(i) ?? displaySelector(i))).ToArray();
    }

    /// <summary>
    /// 异步显示选择器并等待用户输入
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>选择结果，包含选中项或取消标志</returns>
    public async Task<SelectorResult<T>> ShowAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLineReal();
        TerminalHelper.WriteLineReal($"{AnsiStyleEnumConstants.Bold}{_title}{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.WriteLineReal();

        for (var i = 0; i < _items.Length; i++)
        {
            TerminalHelper.WriteLineReal($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleEnumConstants.Reset} {_items[i].DisplayText}");
        }

        TerminalHelper.WriteLineReal();
        TerminalHelper.WriteRawReal($"请输入选择 (1-{_items.Length}, Esc 取消): ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return new SelectorResult<T> { Cancelled = true };
        }

        try
        {
            var input = TerminalHelper.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return new SelectorResult<T> { Cancelled = true };
            }

            if (int.TryParse(input.Trim(), out var index) && index >= 1 && index <= _items.Length)
            {
                return new SelectorResult<T> { Selected = _items[index - 1].Item, Cancelled = false };
            }

            return new SelectorResult<T> { Cancelled = true };
        }
        catch
        {
            return new SelectorResult<T> { Cancelled = true };
        }
    }
}

/// <summary>
/// 选择器结果
/// </summary>
public sealed class SelectorResult<T>
{
    /// <summary>
    /// 选中的项，取消时为默认值
    /// </summary>
    public T Selected { get; init; } = default!;

    /// <summary>
    /// 是否已取消选择
    /// </summary>
    public required bool Cancelled { get; init; }
}
