namespace NativeTui.Core.Repl;

/// <summary>
/// Markdown 消息项（M7-4/M7-5）— 对齐基准 AssistantTextMessage：
/// ● 圆点占 col0（NoSelect minWidth 2，text 色），Markdown 各行从 col2 起（flex row 续行对齐）；
/// 物理行由 MarkdownLayout 按 width-2 缓存布局（宽度变化才重算；高亮随布局一次性算好缓存）；
/// 段样式经 MessageRenderer.MapMarkdownStyle 映射（Code → permission 色，对齐基准 codespan）；
/// 代码行高亮在段写完后按色段覆写前景（M7-5 — 高亮器由宿主注入，null = 降级纯文本）。
/// </summary>
public sealed class MarkdownMessageItem(MarkdownDocument document, ICodeHighlighter? highlighter = null) : IScrollItem
{
    /// <summary>Markdown 正文缩进列（● + 空格，对齐基准 NoSelect minWidth 2）。</summary>
    private const int BodyIndentCols = 2;

    /// <summary>块模型文档（暴露供宿主断言与演示构造）。</summary>
    public MarkdownDocument Document { get; } = document;

    /// <summary>缓存的布局宽度（-1 = 未布局）。</summary>
    private int _layoutWidth = -1;

    /// <summary>缓存的布局结果（行 + 逐行高亮）。</summary>
    private MarkdownLayoutResult? _layout;

    /// <summary>内容高度 = margin 1 + 布局行数（空文档至少 1 行圆点行）。</summary>
    public int MeasureHeight(int width) => 1 + Math.Max(1, LayoutFor(width).Lines.Count);

    /// <summary>渲染：margin 行留空 → ● text 色（col0）→ 各物理行从 col2 起逐段写 → 代码行覆写高亮前景。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var text = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text });
        screen.WriteText(frame.Y + 1, frame.X, Figures.MessageBullet, text, frame.Right);
        var layout = LayoutFor(frame.Width);
        for (var index = 0; index < layout.Lines.Count; index++)
        {
            var lineFrame = frame with { Y = frame.Y + 1 + index, Height = 1 };
            MessageRenderer.RenderMarkdownLine(screen, lineFrame, theme, BodyIndentCols, layout.Lines[index]);
            if (layout.Highlights[index] is { Spans.Count: > 0 } highlight)
            {
                MessageRenderer.ApplyCodeHighlight(screen, lineFrame.Y, frame.X + BodyIndentCols, highlight.Spans);
            }
        }
    }

    /// <summary>按宽度取缓存布局（内容宽 = width - 2 缩进；高亮随布局缓存 — 宽度不变不重算）。</summary>
    private MarkdownLayoutResult LayoutFor(int width)
    {
        if (_layout is null || _layoutWidth != width)
        {
            _layout = MarkdownLayout.LayoutWithHighlights(Document, Math.Max(0, width - BodyIndentCols), highlighter);
            _layoutWidth = width;
        }

        return _layout;
    }
}
