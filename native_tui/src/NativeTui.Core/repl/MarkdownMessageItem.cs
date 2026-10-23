namespace NativeTui.Core.Repl;

/// <summary>
/// Markdown 消息项（M7-4/M7-5）— 对齐基准 AssistantTextMessage：
/// ● 圆点占 col0（NoSelect minWidth 2，text 色），Markdown 各行从 col2 起（flex row 续行对齐）；
/// 物理行由 MarkdownLayout 按 width-2 缓存布局（宽度变化才重算；高亮随布局一次性算好缓存）；
/// 段样式经 MessageRenderer.MapMarkdownStyle 映射（Code → permission 色，对齐基准 codespan）；
/// 代码行高亮在段写完后按色段覆写前景（M7-5 — 高亮器由宿主注入，null = 降级纯文本）。
/// TASK062 结构化：思考时长 &gt; 0 时正文上方 dim 思考摘要行 + Ctrl+O 展开 CoT（同 AssistantMessageItem 语义）。
/// </summary>
public sealed class MarkdownMessageItem(MarkdownDocument document, ICodeHighlighter? highlighter = null, int thinkingDurationMs = 0, string? thinkingContent = null, int messageId = 0) : IScrollItem, ICursorNavigable, ITextMatchable
{
    /// <summary>思考内容行数上限守卫（超限折叠截断提示 — CoT 刷屏防护）。</summary>
    private const int MaxThinkingLines = 200;

    /// <summary>Markdown 正文缩进列（● + 空格，对齐基准 NoSelect minWidth 2）。</summary>
    private const int BodyIndentCols = 2;

    /// <summary>块模型文档（暴露供宿主断言与演示构造）。</summary>
    public MarkdownDocument Document { get; } = document;

    /// <summary>思考总时长 ms（0 = 无思考不渲染思考行）。</summary>
    public int ThinkingDurationMs => thinkingDurationMs;

    /// <summary>CoT 全文（null = 无内容不可展开）。</summary>
    public string? ThinkingContent => thinkingContent;

    /// <summary>消息序号（会话级 — Ctrl+O 定位/transcript 对账）。</summary>
    public int MessageId => messageId;

    /// <summary>思考块展开态（Ctrl+O 切换）。</summary>
    public bool IsThinkingExpanded { get; private set; }

    /// <summary>切换思考块展开态（有 CoT 才有效）。</summary>
    public void ToggleThinking() {
        if (thinkingContent is { Length: > 0 }) {
            IsThinkingExpanded = !IsThinkingExpanded;
        }
    }

    /// <summary>段落/标题文本 + CoT 匹配（transcript 搜索 — 块段拼接为可搜索纯文本）。</summary>
    public bool ContainsText(string query) {
        if (thinkingContent?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) {
            return true;
        }

        foreach (var block in Document.Blocks) {
            var segments = block switch {
                MarkdownParagraph paragraph => paragraph.Segments,
                MarkdownHeading heading => heading.Segments,
                _ => null,
            };
            if (segments is null) {
                continue;
            }

            foreach (var segment in segments) {
                if (segment.Text.Contains(query, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>内容高度 = margin 1 + 布局行数（空文档至少 1 行圆点行）+ 思考摘要行（有思考）+ CoT 行数（展开态）。</summary>
    public int MeasureHeight(int width) =>
        1 + Math.Max(1, LayoutFor(width).Lines.Count)
        + (thinkingDurationMs > 0 ? 1 : 0)
        + (IsThinkingExpanded ? CountThinkingLines() : 0);

    /// <summary>渲染：margin 行留空 → 思考摘要行（有思考）→ CoT（展开态）→ ● text 色（col0）→ 各物理行从 col2 起逐段写 → 代码行覆写高亮前景。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var row = frame.Y + 1;
        if (thinkingDurationMs > 0)
        {
            MessageRenderer.RenderThinkingSummary(screen, frame with { Y = row++, Height = 1 }, theme, thinkingDurationMs, IsThinkingExpanded);
        }

        if (IsThinkingExpanded && thinkingContent is { Length: > 0 })
        {
            var lines = CountThinkingLines();
            MessageRenderer.RenderThinkingContent(screen, frame with { Y = row, Height = lines }, theme, TruncateThinking());
            row += lines;
        }

        var text = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text });
        screen.WriteText(row, frame.X, Figures.MessageBullet, text, frame.Right);
        var layout = LayoutFor(frame.Width);
        for (var index = 0; index < layout.Lines.Count; index++)
        {
            var lineFrame = frame with { Y = row + index, Height = 1 };
            MessageRenderer.RenderMarkdownLine(screen, lineFrame, theme, BodyIndentCols, layout.Lines[index]);
            if (layout.Highlights[index] is { Spans.Count: > 0 } highlight)
            {
                MessageRenderer.ApplyCodeHighlight(screen, lineFrame.Y, frame.X + BodyIndentCols, highlight.Spans);
            }

            // DSG043 P3 — 链接区间平移写入（布局行内容列 → Screen 物理列 + BodyIndent 偏移；
            // SetLinks 替换式幂等，帧重画安全）
            if (layout.Links[index] is { Count: > 0 } spans)
            {
                screen.SetLinks(lineFrame.Y,
                    [.. spans.Select(span => span with { StartCol = span.StartCol + frame.X + BodyIndentCols, EndCol = span.EndCol + frame.X + BodyIndentCols })]);
            }
        }
    }

    /// <summary>CoT 展开行数（含超限截断行）。</summary>
    private int CountThinkingLines() => TruncateThinking().Split('\n').Length;

    /// <summary>CoT 截断守卫 — 超行数上限时截断并附提示行。</summary>
    private string TruncateThinking() {
        if (thinkingContent is not { Length: > 0 })
            return string.Empty;

        var lines = thinkingContent.Split('\n');
        return lines.Length > MaxThinkingLines
            ? string.Join("\n", lines[..MaxThinkingLines]) + $"\n… 思考内容超 {MaxThinkingLines} 行已截断"
            : thinkingContent;
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

    /// <summary>缓存的布局宽度（-1 = 未布局）。</summary>
    private int _layoutWidth = -1;

    /// <summary>缓存的布局结果（行 + 逐行高亮）。</summary>
    private MarkdownLayoutResult? _layout;
}
