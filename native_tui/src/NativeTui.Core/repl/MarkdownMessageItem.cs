namespace NativeTui.Core.Repl;

/// <summary>
/// Markdown 消息项（M7-4）— 对齐基准 AssistantTextMessage：
/// ● 圆点占 col0（NoSelect minWidth 2，text 色），Markdown 各行从 col2 起（flex row 续行对齐）；
/// 物理行由 MarkdownLayout 按 width-2 缓存布局（宽度变化才重算）；
/// 段样式经 MessageRenderer.MapMarkdownStyle 映射（Code → permission 色，对齐基准 codespan）。
/// </summary>
public sealed class MarkdownMessageItem(MarkdownDocument document) : IScrollItem
{
    /// <summary>Markdown 正文缩进列（● + 空格，对齐基准 NoSelect minWidth 2）。</summary>
    private const int BodyIndentCols = 2;

    /// <summary>缓存的布局宽度（-1 = 未布局）。</summary>
    private int _layoutWidth = -1;

    /// <summary>缓存的物理行模型。</summary>
    private IReadOnlyList<IReadOnlyList<MarkdownSegment>> _layout = [];

    /// <summary>内容高度 = margin 1 + 布局行数（空文档至少 1 行圆点行）。</summary>
    public int MeasureHeight(int width) => 1 + Math.Max(1, LayoutFor(width).Count);

    /// <summary>渲染：margin 行留空 → ● text 色（col0）→ 各物理行从 col2 起逐段写。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var text = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text });
        screen.WriteText(frame.Y + 1, frame.X, Figures.MessageBullet, text, frame.Right);
        var layout = LayoutFor(frame.Width);
        for (var index = 0; index < layout.Count; index++)
        {
            MessageRenderer.RenderMarkdownLine(screen, frame with { Y = frame.Y + 1 + index, Height = 1 }, theme, BodyIndentCols, layout[index]);
        }
    }

    /// <summary>按宽度取缓存布局（内容宽 = width - 2 缩进）。</summary>
    private IReadOnlyList<IReadOnlyList<MarkdownSegment>> LayoutFor(int width)
    {
        if (_layoutWidth != width)
        {
            _layout = MarkdownLayout.Layout(document, Math.Max(0, width - BodyIndentCols));
            _layoutWidth = width;
        }

        return _layout;
    }
}
