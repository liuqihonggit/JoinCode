namespace NativeTui.Core.Repl;

/// <summary>
/// 文件快速打开对话框（ctrl+shift+p — 基准 QuickOpenDialog.tsx / FuzzyPicker 数据层+渲染，全部自研）：
/// 构造注入文件清单（IReadOnlyList&lt;string&gt; 相对路径 — 宿主组装，组件零 IO 零进程），
/// 查询输入 → FuzzyMatcher 打分过滤排序（上限 MaxResults）→ ↑↓/ctrl+p/ctrl+n clamp 导航
/// （焦点跟随滚动窗口，对齐 ModelPickerCoordinator 列词语义）→ enter 接受
///（AcceptedFile 契约属性暴露选中文件）/ esc 关闭（IsClosed 置位 + AcceptedFile = null）。
/// 打开文件动作属宿主接线（纯 UI 组件 — 契约属性轮询，无回调无事件）。
/// 渲染对齐 ModelPickerDialog 形态学（permission 色边框 + 标题 + dim footer Byline）：
/// margin + 边框 + 标题 "快速打开" + 查询行（❯ + 查询文本 / dim 占位）+ 空行 + 结果列表
///（焦点行 ❯ suggestion 高亮）+ 溢出行 " … 及 N 项" + 空行 + footer Byline。
/// </summary>
public sealed class QuickOpenDialog : IScrollItem
{
    /// <summary>过滤结果上限（任务契约 — 基准 file suggestions 上限 20 形态对齐 AtFileSuggester）。</summary>
    public const int MaxResults = 20;

    /// <summary>可见行数（基准 QuickOpenDialog VISIBLE_RESULTS = 8）。</summary>
    public const int MaxVisible = 8;

    /// <summary>标题文案。</summary>
    private const string TitleText = "快速打开";

    /// <summary>空查询占位（基准 SearchBox placeholder "Type to search files…"）。</summary>
    private const string QueryPlaceholder = "输入以搜索文件…";

    /// <summary>空查询列表区提示（基准 emptyMessage 空查询分支 "Start typing to search…"）。</summary>
    private const string EmptyQueryListHint = "输入以搜索…";

    /// <summary>无匹配提示（基准 emptyMessage 非空分支 "No matching files"）。</summary>
    private const string NoResultsText = "无匹配文件";

    /// <summary>footer Byline（任务契约文案 — 基准 FuzzyPicker Byline 快捷键行形态）。</summary>
    private static readonly string FooterText =
        $"{Figures.UpArrow}{Figures.DownArrow} 导航{Figures.MiddotSeparator}enter 打开{Figures.MiddotSeparator}esc 关闭";

    /// <summary>固定行数 = margin 1 + 边框 1 + 标题 1 + 查询行 1 + 空行 1 + 空行 1 + footer 1。</summary>
    private const int FixedRows = 7;

    private readonly IReadOnlyList<string> _files;
    private List<string> _results = [];
    private string _query = string.Empty;
    private int _selectedIndex;
    private bool _closed;
    private string? _acceptedFile;

    /// <summary>创建快速打开对话框 — files 为工作区相对路径清单（宿主组装注入；组件零 IO）。</summary>
    /// <param name="files">候选文件清单（/ 分隔相对路径；空清单合法 — 显示输入提示态）。</param>
    public QuickOpenDialog(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
        RefreshResults();
    }

    /// <summary>当前查询文本（字符/粘贴追加，退格去尾）。</summary>
    public string Query => _query;

    /// <summary>过滤结果（FuzzyMatcher 打分降序 → 路径短优先 → 字母序，上限 MaxResults）。</summary>
    public IReadOnlyList<string> Results => _results;

    /// <summary>选中索引（clamp 于 [0, Results.Count-1]；查询变化归零）。</summary>
    public int SelectedIndex => _selectedIndex;

    /// <summary>焦点项（无结果为 null）。</summary>
    public string? SelectedFile => _selectedIndex < _results.Count ? _results[_selectedIndex] : null;

    /// <summary>是否已关闭（enter 接受或 esc 取消 — 宿主消费后移除对话框项）。</summary>
    public bool IsClosed => _closed;

    /// <summary>enter 接受的文件（契约属性 — esc 关闭或未选择为 null；宿主据此执行打开动作）。</summary>
    public string? AcceptedFile => _acceptedFile;

    /// <summary>可见条数 = min(MaxVisible, Results.Count)。</summary>
    public int VisibleCount => Math.Min(MaxVisible, _results.Count);

    /// <summary>折叠条数 = max(0, n - MaxVisible)（" … 及 N 项" 溢出行依据）。</summary>
    public int HiddenCount => Math.Max(0, _results.Count - VisibleCount);

    /// <summary>滚动窗口起点（焦点跟随 — 对齐 ModelPickerCoordinator 列表窗口语义）。</summary>
    public int WindowStart { get; private set; }

    /// <summary>
    /// 键位路由（模态契约 — 对话框激活期间全部吞掉返回 true，对齐 HandleModelPickerKey 模态语义）：
    /// 字符/粘贴进查询（Ctrl 修饰除外）、退格去尾、↑/ctrl+p 上移、↓/ctrl+n 下移、
    /// enter 接受焦点项（无结果接受 null）、esc 关闭；其余键（Tab/Delete/Home 等）静默吞掉。
    /// 已关闭返回 false（宿主不应再路由 — 防御契约）。
    /// </summary>
    /// <param name="key">按键事件。</param>
    /// <returns>true = 已消费（模态吞掉）；false = 对话框已关闭（宿主接管后续动作）。</returns>
    public bool TryHandleKey(KeyPress key)
    {
        if (_closed)
        {
            return false;
        }

        if (key.Kind == KeyKind.Escape)
        {
            Close();
            return true;
        }

        if (key.Kind == KeyKind.Return)
        {
            AcceptSelection();
            return true;
        }

        if (IsNavigateUp(key))
        {
            MoveFocus(-1);
            return true;
        }

        if (IsNavigateDown(key))
        {
            MoveFocus(1);
            return true;
        }

        if (key.Kind == KeyKind.Backspace)
        {
            TrimQueryTail();
            return true;
        }

        if (IsTextInput(key))
        {
            AppendQuery(key.Input);
            return true;
        }

        return true;
    }

    /// <summary>内容高度 = 固定 7 行 + 列表区（空态 1 行 / 可见 N 行）+ 溢出 0/1；关闭后 0。</summary>
    public int MeasureHeight(int width) =>
        _closed ? 0 : FixedRows + ListRows + OverflowRows;

    /// <summary>渲染：按固定行序绘制（关闭后不画 — 宿主消费 IsClosed 后移除对话框项）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        if (_closed)
        {
            return;
        }

        var row = frame.Y + 1;
        MessageRenderer.RenderPermissionBorder(screen, RowFrame(frame, row++), theme);
        MessageRenderer.RenderModelTitle(screen, RowFrame(frame, row++), theme, TitleText);
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

    /// <summary>列表区行数（MeasureHeight/Render 一致性锚点 — 空态 1 行提示占位）。</summary>
    private int ListRows => _results.Count == 0 ? 1 : VisibleCount;

    /// <summary>溢出行数（HiddenCount &gt; 0 时 1）。</summary>
    private int OverflowRows => HiddenCount > 0 ? 1 : 0;

    /// <summary>上移键 = ↑ 或 ctrl+p（基准 FuzzyPicker e.ctrl &amp;&amp; key === 'p'）。</summary>
    private static bool IsNavigateUp(KeyPress key) =>
        key.Kind == KeyKind.UpArrow || (key.Ctrl && key.Kind == KeyKind.Char && key.Input == "p");

    /// <summary>下移键 = ↓ 或 ctrl+n（基准 FuzzyPicker e.ctrl &amp;&amp; key === 'n'）。</summary>
    private static bool IsNavigateDown(KeyPress key) =>
        key.Kind == KeyKind.DownArrow || (key.Ctrl && key.Kind == KeyKind.Char && key.Input == "n");

    /// <summary>文本输入键 = 无 Ctrl 修饰的字符 / 粘贴块（Input 携带原文）。</summary>
    private static bool IsTextInput(KeyPress key) =>
        !key.Ctrl && key.Kind is KeyKind.Char or KeyKind.Paste;

    /// <summary>重过滤 — FuzzyMatcher 打分（&gt; 0 保留）分降序 → 路径短优先 → 字母序，
    /// 上限 MaxResults；选中与窗口归零（查询变化选中归零 — 对齐基准 FuzzyPicker setFocusedIndex(0)）。</summary>
    private void RefreshResults()
    {
        _results = [.. _files
            .Select(file => (File: file, Score: FuzzyMatcher.Match(_query, file)))
            .Where(entry => entry.Score > FuzzyMatcher.ScoreNoMatch)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.File.Length)
            .ThenBy(entry => entry.File, StringComparer.Ordinal)
            .Take(MaxResults)
            .Select(entry => entry.File)];
        _selectedIndex = 0;
        WindowStart = 0;
    }

    /// <summary>追加查询文本（字符或粘贴块）并重过滤。</summary>
    private void AppendQuery(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        _query += text;
        RefreshResults();
    }

    /// <summary>退格去尾并重过滤（空查询静默吞掉不关闭 — 基准 backspaceExitsOnEmpty=false）。</summary>
    private void TrimQueryTail()
    {
        if (_query.Length == 0)
        {
            return;
        }

        _query = _query[..^1];
        RefreshResults();
    }

    /// <summary>焦点移动 — clamp 不环绕（列表语义，对齐 ModelPickerCoordinator.MoveFocus）+ 滚动窗口跟随。</summary>
    private void MoveFocus(int delta)
    {
        if (_results.Count == 0)
        {
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _results.Count - 1);
        if (_selectedIndex < WindowStart)
        {
            WindowStart = _selectedIndex;
        }
        else if (_selectedIndex >= WindowStart + VisibleCount)
        {
            WindowStart = _selectedIndex - VisibleCount + 1;
        }
    }

    /// <summary>enter 接受 — 记录焦点文件（无结果为 null）并置关闭。</summary>
    private void AcceptSelection()
    {
        _acceptedFile = SelectedFile;
        _closed = true;
    }

    /// <summary>esc 关闭 — 接受文件置 null。</summary>
    private void Close()
    {
        _acceptedFile = null;
        _closed = true;
    }

    /// <summary>查询行渲染 — " ❯ " + 查询文本（超宽截断尾缀 …）；空查询显示 dim 占位提示。</summary>
    private void RenderQueryRow(Screen screen, Rect frame)
    {
        if (_query.Length == 0)
        {
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            screen.WriteText(frame.Y, frame.X, $" {Figures.Pointer} {QueryPlaceholder}", dim, frame.Right);
            return;
        }

        screen.WriteText(frame.Y, frame.X, $" {Figures.Pointer} ", 0, frame.Right);
        screen.WriteText(frame.Y, frame.X + 3, TruncateToDisplayWidth(_query, Math.Max(1, frame.Width - 4)), 0, frame.Right);
    }

    /// <summary>列表区渲染 — 空态 1 行 dim 提示（空查询/无匹配分支文案）；有结果按滚动窗口绘制可见行。</summary>
    /// <returns>下一渲染行号。</returns>
    private int RenderListRows(Screen screen, Rect frame, int row, Theme theme)
    {
        if (_results.Count == 0)
        {
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            var hint = _query.Length == 0 ? EmptyQueryListHint : NoResultsText;
            screen.WriteText(row, frame.X, $" {hint}", dim, frame.Right);
            return row + 1;
        }

        for (var i = 0; i < VisibleCount; i++)
        {
            var index = WindowStart + i;
            RenderResultRow(screen, RowFrame(frame, row++), theme, _results[index], index == _selectedIndex);
        }

        return row;
    }

    /// <summary>结果行渲染（对齐 RenderModelListRow 形态，无序号 — 路径列 col 2 起）：
    /// 焦点 = ❯ suggestion + 空格 + 路径 suggestion 色；非焦点 = 2 空格缩进 + 路径默认样式。</summary>
    private static void RenderResultRow(Screen screen, Rect frame, Theme theme, string path, bool focused)
    {
        var text = TruncateToDisplayWidth(path, Math.Max(1, frame.Width - 5));
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

    /// <summary>溢出行渲染 — 1 缩进 dim " … 及 N 项"（对齐 RenderModelOverflow 中文形态）。</summary>
    private void RenderOverflowRow(Screen screen, Rect frame)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $" … 及 {HiddenCount} 项", dim, frame.Right);
    }

    /// <summary>显示宽截断 — 超宽时截到 maxWidth-1 显示列 + 尾缀 "…"（RuneWidth 计量，CJK 2 列）。</summary>
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
