namespace NativeTui.Core.Repl;

/// <summary>
/// 建议列表渲染 — 对齐基准 PromptInputFooterSuggestions.tsx：
/// 最多 6 条可见（MAX_VISIBLE_ITEMS）；两列布局（"/命令名" + 描述，命令列宽固定防布局跳动）；
/// 选中项 Suggestion 色 / 未选中 dim；滚动窗口以选中项为中心（钳制首尾）。
/// </summary>
public sealed class SuggestionBox
{
    /// <summary>可见条数上限（基准非全屏 maxVisibleItems = min(6, rows-3)，TUI 固定 6）。</summary>
    public const int MaxVisibleItems = 6;

    /// <summary>可见高度 = min(条数, 上限)。</summary>
    public int MeasureHeight(int itemCount) => Math.Min(itemCount, MaxVisibleItems);

    /// <summary>渲染建议列表 — frame 内自顶向下每行一条；选中项 Suggestion 色，未选中 dim；
    /// 命令模式命令名带 / 前缀自 frame.X 起，参数模式裸参数值；描述自 frame.X + nameColumnWidth 起（超宽截断）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme, IReadOnlyList<CommandSuggestion> items, int selectedIndex, int nameColumnWidth, bool argumentMode = false)
    {
        if (items.Count == 0)
        {
            return;
        }

        var visible = Math.Min(MeasureHeight(items.Count), frame.Height);
        var startIndex = Math.Clamp(selectedIndex - visible / 2, 0, items.Count - visible);
        var selectedStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Suggestion });
        var dimStyle = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var prefix = argumentMode ? string.Empty : "/";

        for (var row = 0; row < visible; row++)
        {
            var index = startIndex + row;
            var item = items[index];
            var style = index == selectedIndex ? selectedStyle : dimStyle;
            screen.WriteText(frame.Y + row, frame.X, $"{prefix}{item.Name}", style, frame.X + nameColumnWidth);
            if (item.Description.Length > 0)
            {
                screen.WriteText(frame.Y + row, frame.X + nameColumnWidth, item.Description, style, frame.Right);
            }
        }
    }
}
