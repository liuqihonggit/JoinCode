namespace JoinCode.Cli;

// ─── Selector ───

/// <summary>
/// 交互式选择器 — CLI 简化版，使用数字选择替代 TUI 上下键
/// </summary>
public sealed class Selector<T> {
    private readonly string _title;
    private readonly (T Item, string DisplayText, string SearchKey, string DetailText)[] _items;
    private readonly bool _enableSearch;

    /// <summary>
    /// 构造选择器实例，使用预格式化的项元组数组
    /// </summary>
    /// <param name="title">选择器标题</param>
    /// <param name="items">项元组数组，每项包含原始对象、显示文本、搜索键</param>
    public Selector(string title, (T Item, string DisplayText, string SearchKey)[] items) {
        _title = title;
        _items = items.Select(i => (i.Item, i.DisplayText, i.SearchKey, string.Empty)).ToArray();
    }

    /// <summary>
    /// 构造选择器实例，使用显示选择器和可选搜索选择器从原始项数组生成项元组
    /// </summary>
    /// <param name="title">选择器标题</param>
    /// <param name="items">原始项数组</param>
    /// <param name="displaySelector">将项转换为显示文本的委托（主行）</param>
    /// <param name="searchSelector">将项转换为搜索键的可选委托，缺省时使用显示文本</param>
    /// <param name="enableSearch">是否启用关键字过滤（TASK069-C2 — 输入非数字即按关键字过滤后重列）</param>
    /// <param name="detailSelector">将项转换为副行文本的可选委托（TASK069-C1 — dim 缩进打印在主行下方，缺省不占行）</param>
    public Selector(string title, T[] items, Func<T, string> displaySelector, Func<T, string>? searchSelector = null, bool enableSearch = false, Func<T, string>? detailSelector = null) {
        _title = title;
        _items = items.Select(i => (i, displaySelector(i), searchSelector?.Invoke(i) ?? displaySelector(i), detailSelector?.Invoke(i) ?? string.Empty)).ToArray();
        _enableSearch = enableSearch;
    }

    /// <summary>
    /// 异步显示选择器并等待用户输入 — 数字即选中；启用搜索时非数字输入按关键字过滤后重新列出
    /// （空回车/Esc 取消，过滤不改变取消语义）。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>选择结果，包含选中项或取消标志</returns>
    public async Task<SelectorResult<T>> ShowAsync(CancellationToken ct = default) {
        await Task.CompletedTask.ConfigureAwait(false);

        var visible = _items;
        string? keyword = null;
        while (true) {
            PrintList(visible, keyword);

            var prompt = _enableSearch
                ? $"请输入选择 (1-{visible.Length})，或输入关键字过滤（空回车取消）: "
                : $"请输入选择 (1-{_items.Length}, Esc 取消): ";
            TerminalHelper.WriteRawReal(prompt);

            if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
                return new SelectorResult<T> { Cancelled = true };
            }

            string? input;
            try {
                input = TerminalHelper.ReadLine();
            } catch {
                return new SelectorResult<T> { Cancelled = true };
            }

            if (string.IsNullOrWhiteSpace(input)) {
                return new SelectorResult<T> { Cancelled = true };
            }

            var trimmed = input.Trim();
            if (int.TryParse(trimmed, out var index) && index >= 1 && index <= visible.Length) {
                return new SelectorResult<T> { Selected = visible[index - 1].Item, Cancelled = false };
            }

            if (!_enableSearch) {
                return new SelectorResult<T> { Cancelled = true };
            }

            // 非数字 → 当作过滤关键字（再次循环重列；命中 0 项时提示后可继续改关键字）
            keyword = trimmed;
            visible = SelectorFilter.Apply(_items, static entry => entry.SearchKey, keyword);
        }
    }

    /// <summary>列出当前可见项（过滤态追加来源提示行 — 让用户知道自己看的是子集）。</summary>
    private void PrintList((T Item, string DisplayText, string SearchKey, string DetailText)[] visible, string? keyword) {
        TerminalHelper.WriteLineReal();
        TerminalHelper.WriteLineReal($"{AnsiStyleEnumConstants.Bold}{_title}{AnsiStyleEnumConstants.Reset}");
        if (keyword is not null) {
            var hint = visible.Length == 0
                ? $"  过滤「{keyword}」无匹配，可换个关键字"
                : $"  过滤「{keyword}」：{visible.Length}/{_items.Length} 项";
            TerminalHelper.WriteLineReal($"{TerminalColors.Muted}{hint}{AnsiStyleEnumConstants.Reset}");
        }

        TerminalHelper.WriteLineReal();

        for (var i = 0; i < visible.Length; i++) {
            TerminalHelper.WriteLineReal($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleEnumConstants.Reset} {visible[i].DisplayText}");
            if (visible[i].DetailText.Length > 0) {
                TerminalHelper.WriteLineReal($"     {TerminalColors.Muted}{visible[i].DetailText}{AnsiStyleEnumConstants.Reset}");
            }
        }

        TerminalHelper.WriteLineReal();
    }
}

/// <summary>
/// 选择器结果
/// </summary>
public sealed class SelectorResult<T> {
    /// <summary>
    /// 选中的项，取消时为默认值
    /// </summary>

    [AllowNull]
    public T Selected { get; init; } = default;

    /// <summary>
    /// 是否已取消选择
    /// </summary>
    public required bool Cancelled { get; init; }
}