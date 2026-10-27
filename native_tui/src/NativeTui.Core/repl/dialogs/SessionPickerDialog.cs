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
/// ↑↓ clamp 导航（焦点跟随滚动窗口，对齐 ModelPickerCoordinator 列词语义）→ enter 接受
///（SelectionTask TaskCompletionSource 完成回传选中 SessionId）/ esc 取消（完成 null）。
/// 渲染对齐 QuickOpenDialog/ModelPickerDialog 形态学（permission 色边框 + 标题 + dim footer）：
/// margin + 边框 + 标题 + 空行 + 会话列表（焦点行 ❯ suggestion 高亮）+ 溢出行 + 空行 + footer。
/// TCS 桥跨线程安全 — 引擎回合线程 await SelectionTask，TUI 主线程 TryHandleKey 完成TCS。
/// </summary>
public sealed class SessionPickerDialog : IScrollItem
{
    /// <summary>可见行数（对齐 ModelPickerCoordinator.MaxVisible=10）。</summary>
    public const int MaxVisible = 10;

    /// <summary>footer 快捷键行（对齐 QuickOpenDialog FooterText 形态）。</summary>
    private static readonly string FooterText =
        $"{Figures.UpArrow}{Figures.DownArrow} 选择{Figures.MiddotSeparator}enter 恢复{Figures.MiddotSeparator}esc 取消";

    /// <summary>固定行数 = margin 1 + 边框 1 + 标题 1 + 空行 1 + 空行 1 + footer 1。</summary>
    private const int FixedRows = 6;

    private readonly IReadOnlyList<SessionPickerItem> _items;
    private readonly string _title;
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
    }

    /// <summary>选择结果任务 — 命令侧 await 此任务等待用户选择。Enter 完成选中 SessionId，Esc 完成 null。</summary>
    public Task<string?> SelectionTask => _tcs.Task;

    /// <summary>是否已关闭（enter 接受或 esc 取消 — 宿主消费后移除对话框项）。</summary>
    public bool IsClosed => _closed;

    /// <summary>选中索引（clamp 于 [0, items.Count-1]）。</summary>
    public int SelectedIndex => _selectedIndex;

    /// <summary>可见条数 = min(MaxVisible, items.Count)。</summary>
    public int VisibleCount => Math.Min(MaxVisible, _items.Count);

    /// <summary>折叠条数 = max(0, n - MaxVisible)。</summary>
    public int HiddenCount => Math.Max(0, _items.Count - VisibleCount);

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
    private int ListRows => _items.Count == 0 ? 1 : VisibleCount;

    /// <summary>溢出行数（HiddenCount &gt; 0 时 1）。</summary>
    private int OverflowRows => HiddenCount > 0 ? 1 : 0;

    /// <summary>焦点移动 — clamp 不环绕 + 滚动窗口跟随。</summary>
    private void MoveFocus(int delta)
    {
        if (_items.Count == 0)
        {
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _items.Count - 1);
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
        var selectedId = _selectedIndex < _items.Count ? _items[_selectedIndex].SessionId : null;
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
        if (_items.Count == 0)
        {
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            screen.WriteText(row, frame.X, " 无可恢复的会话", dim, frame.Right);
            return row + 1;
        }

        for (var i = 0; i < VisibleCount; i++)
        {
            var index = WindowStart + i;
            RenderItemRow(screen, RowFrame(frame, row++), theme, _items[index], index == _selectedIndex);
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
