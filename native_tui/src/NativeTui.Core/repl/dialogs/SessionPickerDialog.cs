namespace NativeTui.Core.Repl;

/// <summary>
/// 会话选择器条目 — NativeTui.Core 层 DTO（不依赖 abstractions 层）。
/// ReplShell/Program 负责把 abstractions 层 SessionPickerEntry 转换为本类型。
/// </summary>
/// <param name="SessionId">会话唯一标识（完整 ID，选择后回传命令侧）。</param>
/// <param name="Preview">会话预览文本（customTitle 或 ID 前缀）。</param>
/// <param name="TimeAgo">相对时间描述（如"22 小时前"）。</param>
public sealed record SessionPickerItem(string SessionId, string Preview, string TimeAgo);

/// <summary>
/// TUI 原生会话选择器对话框（/session /resume 命令选择历史会话 — 替代 CLI 简化版 Selector）：
/// 构造注入会话清单（IReadOnlyList&lt;SessionPickerItem&gt; — 宿主组装 + 跨层转换，组件零 IO），
/// **输入即模糊搜索**（TASK069-C2 — 与 QuickOpenDialog 同一 FuzzyMatcher 数据层：追加/退格即刻
/// 打分过滤、命中降序，选中与窗口归零）→ ↑↓ clamp 导航（焦点跟随滚动窗口，对齐
/// ModelPickerCoordinator 列词语义）→ enter 接受（SelectionTask TaskCompletionSource 完成回传
/// 选中 SessionId）/ esc 取消（完成 null）。
/// 渲染对齐 QuickOpenDialog/ModelPickerDialog 形态学（permission 色边框 + 标题 + dim footer）：
/// margin + 边框 + 标题 + 查询行 + 空行 + 会话列表（焦点行 ❯ suggestion 高亮）+ 溢出行 + 空行 + footer。
/// TCS 桥跨线程安全 — 引擎回合线程 await SelectionTask，TUI 主线程 TryHandleKey 完成TCS。
/// </summary>
public sealed class SessionPickerDialog : IScrollItem
{
    /// <summary>可见行数（对齐 ModelPickerCoordinator.MaxVisible=10）。</summary>
    public const int MaxVisible = 10;

    /// <summary>footer 快捷键行（对齐 QuickOpenDialog FooterText 形态）。</summary>
    private static readonly string FooterText =
        $"{Figures.UpArrow}{Figures.DownArrow} 选择{Figures.MiddotSeparator}enter 恢复{Figures.MiddotSeparator}esc 取消";

    /// <summary>固定行数 = margin 1 + 边框 1 + 标题 1 + 查询 1 + 空行 1 + 空行 1 + footer 1。</summary>
    private const int FixedRows = 7;

    /// <summary>查询行占位文案（空查询时 dim 显示）。</summary>
    private const string QueryPlaceholder = "输入以搜索会话…";

    /// <summary>无匹配提示（有关键字但零命中）。</summary>
    private const string NoMatchText = " 无匹配会话";

    private readonly IReadOnlyList<SessionPickerItem> _items;
    private readonly string _title;
    private IReadOnlyList<SessionPickerItem> _filtered;
    private string _query = string.Empty;
    private readonly TaskCompletionSource<string?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _selectedIndex;
    private bool _closed;

    /// <summary>创建会话选择器对话框 — items 为已排序已过滤的会话清单（宿主组装注入）。</summary>
    /// <param name="items">会话条目清单（空清单合法 — 显示空态提示）。</param>
    /// <param name="title">选择器标题（如"选择会话（当前仓库）"）。</param>
    public SessionPickerDialog(IReadOnlyList<SessionPickerItem> items, string title)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
        _title = title;
        _filtered = items;
    }

    /// <summary>当前查询（空 = 全量清单，原顺序）。</summary>
    public string Query => _query;

    /// <summary>过滤后条数（与渲染同源 — 宿主/测试断言面）。</summary>
    public int FilteredCount => _filtered.Count;

    /// <summary>选择结果任务 — 命令侧 await 此任务等待用户选择。Enter 完成选中 SessionId，Esc 完成 null。</summary>
    public Task<string?> SelectionTask => _tcs.Task;

    /// <summary>是否已关闭（enter 接受或 esc 取消 — 宿主消费后移除对话框项）。</summary>
    public bool IsClosed => _closed;

    /// <summary>选中索引（clamp 于 [0, items.Count-1]）。</summary>
    public int SelectedIndex => _selectedIndex;

    /// <summary>可见条数 = min(MaxVisible, 过滤后条数)。</summary>
    public int VisibleCount => Math.Min(MaxVisible, _filtered.Count);

    /// <summary>折叠条数 = max(0, n - MaxVisible)。</summary>
    public int HiddenCount => Math.Max(0, _filtered.Count - VisibleCount);

    /// <summary>滚动窗口起点（焦点跟随）。</summary>
    public int WindowStart { get; private set; }

    /// <summary>
    /// 键位路由（模态契约 — 对话框激活期间全部吞掉返回 true）：
    /// ↑ 上移、↓ 下移、enter 接受焦点项、esc 取消；其余键静默吞掉。已关闭返回 false。
    /// </summary>
    public bool TryHandleKey(KeyPress key)
    {
        if (_closed)
        {
            return false;
        }

        if (key.Kind == KeyKind.Escape)
        {
            Cancel();
            return true;
        }

        if (key.Kind == KeyKind.Return)
        {
            AcceptSelection();
            return true;
        }

        if (key.Kind == KeyKind.UpArrow)
        {
            MoveFocus(-1);
            return true;
        }

        if (key.Kind == KeyKind.DownArrow)
        {
            MoveFocus(1);
            return true;
        }

        if (key.Kind == KeyKind.Backspace)
        {
            TrimQueryTail();
            return true;
        }

        if (!key.Ctrl && key.Kind is KeyKind.Char or KeyKind.Paste)
        {
            AppendQuery(key.Input);
            return true;
        }

        return true;
    }

    /// <summary>内容高度 = 固定 6 行 + 列表区（空态 1 行 / 可见 N 行）+ 溢出 0/1；关闭后 0。</summary>
    public int MeasureHeight(int width) =>
        _closed ? 0 : FixedRows + ListRows + OverflowRows;

    /// <summary>渲染：按固定行序绘制（关闭后不画）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        if (_closed)
        {
            return;
        }

        var row = frame.Y + 1;
        MessageRenderer.RenderPermissionBorder(screen, RowFrame(frame, row++), theme);
        MessageRenderer.RenderModelTitle(screen, RowFrame(frame, row++), theme, _title);
        RenderQueryRow(screen, RowFrame(frame, row++));
        row++;
        row = RenderListRows(screen, frame, row, theme);
        if (HiddenCount > 0)
        {
            RenderOverflowRow(screen, RowFrame(frame, row++));
        }

        row++;
        MessageRenderer.RenderModelShortcuts(screen, RowFrame(frame, row), theme, FooterText);
    }

    /// <summary>列表区行数（空态 1 行提示占位）。</summary>
    private int ListRows => _filtered.Count == 0 ? 1 : VisibleCount;

    /// <summary>溢出行数（HiddenCount &gt; 0 时 1）。</summary>
    private int OverflowRows => HiddenCount > 0 ? 1 : 0;

    /// <summary>焦点移动 — clamp 不环绕 + 滚动窗口跟随。</summary>
    private void MoveFocus(int delta)
    {
        if (_filtered.Count == 0)
        {
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _filtered.Count - 1);
        if (_selectedIndex < WindowStart)
        {
            WindowStart = _selectedIndex;
        }
        else if (_selectedIndex >= WindowStart + VisibleCount)
        {
            WindowStart = _selectedIndex - VisibleCount + 1;
        }
    }

    /// <summary>enter 接受 — TCS 完成选中 SessionId（无项时 null）并置关闭。</summary>
    private void AcceptSelection()
    {
        var selectedId = _selectedIndex < _filtered.Count ? _filtered[_selectedIndex].SessionId : null;
        _closed = true;
        _tcs.TrySetResult(selectedId);
    }

    /// <summary>esc 取消 — TCS 完成 null 并置关闭。</summary>
    private void Cancel()
    {
        _closed = true;
        _tcs.TrySetResult(null);
    }

    /// <summary>列表区渲染 — 空态 1 行 dim 提示；有项按滚动窗口绘制可见行。</summary>
    private int RenderListRows(Screen screen, Rect frame, int row, Theme theme)
    {
        if (_filtered.Count == 0)
        {
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            var text = _items.Count == 0 ? " 无可恢复的会话" : NoMatchText;
            screen.WriteText(row, frame.X, text, dim, frame.Right);
            return row + 1;
        }

        for (var i = 0; i < VisibleCount; i++)
        {
            var index = WindowStart + i;
            RenderItemRow(screen, RowFrame(frame, row++), theme, _filtered[index], index == _selectedIndex);
        }

        return row;
    }

    /// <summary>会话行渲染（对齐 QuickOpenDialog RenderResultRow 形态）：
    /// 焦点 = ❯ suggestion + 空格 + 显示文本 suggestion 色；非焦点 = 2 空格缩进 + 文本默认样式。
    /// 显示文本格式：[ID前8位...] TimeAgo · Preview</summary>
    private static void RenderItemRow(Screen screen, Rect frame, Theme theme, SessionPickerItem item, bool focused)
    {
        var idPrefix = item.SessionId.Length <= 8 ? item.SessionId : item.SessionId[..8];
        var displayText = $"[{idPrefix}...] {item.TimeAgo} · {item.Preview}";
        var text = TruncateToDisplayWidth(displayText, Math.Max(1, frame.Width - 5));
        if (focused)
        {
            var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Suggestion });
            screen[frame.Y, frame.X] = new Cell(new Rune('❯'), suggestion, CellWidth.Default);
            screen.WriteText(frame.Y, frame.X + 1, " ", suggestion, frame.Right);
            screen.WriteText(frame.Y, frame.X + 2, text, suggestion, frame.Right);
            return;
        }

        screen.WriteText(frame.Y, frame.X, "  ", 0, frame.Right);
        screen.WriteText(frame.Y, frame.X + 2, text, 0, frame.Right);
    }

    /// <summary>查询行渲染 — 空查询 dim 占位，有查询 " ❯ {query}"（对齐 QuickOpenDialog）。</summary>
    private void RenderQueryRow(Screen screen, Rect frame)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        if (_query.Length == 0)
        {
            screen.WriteText(frame.Y, frame.X, $" {Figures.Pointer} {QueryPlaceholder}", dim, frame.Right);
            return;
        }

        var text = TruncateToDisplayWidth(_query, Math.Max(1, frame.Width - 4));
        screen.WriteText(frame.Y, frame.X, $" {Figures.Pointer} {text}", 0, frame.Right);
    }

    /// <summary>追加查询文本（字符或粘贴块）并重过滤。</summary>
    private void AppendQuery(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        _query += text;
        RefreshFilter();
    }

    /// <summary>退格去尾并重过滤（空查询静默吞掉不关闭）。</summary>
    private void TrimQueryTail()
    {
        if (_query.Length == 0)
        {
            return;
        }

        _query = _query[..^1];
        RefreshFilter();
    }

    /// <summary>重过滤 — 空查询还原全量原顺序；否则 FuzzyMatcher 打分（&gt; 0 保留）降序，
    /// 同分按原序（时间新优先的自然顺序保持）；选中与窗口归零（查询变化焦点回首个结果）。</summary>
    private void RefreshFilter()
    {
        if (_query.Length == 0)
        {
            _filtered = _items;
        }
        else
        {
            _filtered = [.. _items
                .Select((item, index) => (Item: item, Index: index, Score: FuzzyMatcher.Match(_query, SearchKeyOf(item))))
                .Where(entry => entry.Score > FuzzyMatcher.ScoreNoMatch)
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Item)];
        }

        _selectedIndex = 0;
        WindowStart = 0;
    }

    /// <summary>模糊匹配键 — 会话 ID + 预览文本（屏幕上可见的两列信息）。</summary>
    private static string SearchKeyOf(SessionPickerItem item) => $"{item.SessionId} {item.Preview}";

    /// <summary>溢出行渲染 — 1 缩进 dim " … 及 N 项"。</summary>
    private void RenderOverflowRow(Screen screen, Rect frame)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $" … 及 {HiddenCount} 项", dim, frame.Right);
    }

    /// <summary>显示宽截断 — 超宽时截到 maxWidth-1 显示列 + 尾缀 "…"。</summary>
    private static string TruncateToDisplayWidth(string text, int maxWidth)
    {
        if (RuneWidth.OfText(text) <= maxWidth)
        {
            return text;
        }

        var width = 0;
        var end = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var advance = RuneWidth.Of(rune);
            if (width + advance > maxWidth - 1)
            {
                break;
            }

            width += advance;
            end += rune.Utf16SequenceLength;
        }

        return string.Concat(text.AsSpan(0, end), "…");
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}
