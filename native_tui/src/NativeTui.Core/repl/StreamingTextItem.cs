namespace NativeTui.Core.Repl;

/// <summary>
/// 流式正文项（M8-1 逐字流式 — TASK064 方案 A；方案 B 块级增量升级 — 流式期 Markdown 实时渲染）：
/// 回合进行中 assistant 正文逐字上屏，对齐基准 Messages.tsx 尾部 ⎿ 前缀行渲染 StreamingMarkdown
///（块边界切分：稳定前缀完整块走 MarkdownLayout 布局 + 段样式渲染，增长尾纯文本 —
/// OnDone 由 Driver 原位替换为完整 Markdown/Assistant 项 ⏺ 形态，视觉无跳变）。
/// ⎿ 前缀整体 dim（对齐 ToolResultItem "  ⎿  " 先例）；续行缩进对齐内容列；
/// ScrollBox 每帧全量重渲（MeasureHeight/Render 每帧调用）— Append 置脏，帧内首次访问惰性投影
///（同帧 Measure+Render 共享投影与行模型缓存；宽度变化才重布局 — 对齐 MarkdownMessageItem 缓存模式）。
/// projector null = 降级纯文本逐字（M8-1 行为完整保留）；流式期不做代码高亮
///（与基准一致：增量期无高亮，落定后 MarkdownMessageItem 全量高亮）。
/// </summary>
public sealed class StreamingTextItem : IScrollItem, ITextMatchable
{
    /// <summary>内容显示列（"  ⎿  " 前缀 5 rune 但 ⎿ 宽字符占 2 显示列 = 6 显示列；续行 6 空格对齐）。</summary>
    private const int ContentIndentCols = 6;

    private readonly StringBuilder _buffer = new();

    /// <summary>块级增量投影器（方案 B 卫星注入；null = 纯文本降级）。</summary>
    private readonly IStreamingMarkdownProjector? _projector;

    /// <summary>投影缓存（脏时刷新 — Append 置脏；projector 缺席恒 null）。</summary>
    private StreamingMarkdownProjection? _projection;

    /// <summary>投影脏标记（Append 后置位 — 帧内首次 LinesFor 刷新；projector 缺席恒 false）。</summary>
    private bool _projectionDirty;

    /// <summary>稳定段布局行缓存（投影刷新/宽度变化失效 — 对齐 MarkdownMessageItem.LayoutFor 缓存模式）。</summary>
    private IReadOnlyList<IReadOnlyList<MarkdownSegment>>? _stableLines;

    /// <summary>稳定段布局缓存宽度（-1 = 未布局）。</summary>
    private int _stableLinesWidth = -1;

    /// <summary>完整行模型缓存（稳定布局行 + gap 空行 + 尾行；投影刷新/宽度变化失效）。</summary>
    private IReadOnlyList<IReadOnlyList<MarkdownSegment>>? _builtLines;

    /// <summary>行模型缓存宽度（-1 = 未构建）。</summary>
    private int _builtLinesWidth = -1;

    /// <summary>创建流式项 — projector = 块级增量投影器（null = 纯文本降级 — M8-1 行为）。</summary>
    public StreamingTextItem(IStreamingMarkdownProjector? projector = null)
    {
        _projector = projector;
        _projectionDirty = projector is not null;
    }

    /// <summary>当前累积文本（测试断言面 + Driver 替换/定格语义消费）。</summary>
    public string Text => _buffer.ToString();

    /// <summary>流式缓冲文本匹配（transcript 搜索）。</summary>
    public bool ContainsText(string query) => _buffer.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>追加一段增量文本（引擎 OnText 粒度 — 空串无操作；置投影脏）。</summary>
    public void Append(string text) {
        if (text.Length > 0) {
            _buffer.Append(text);
            _projectionDirty = true;
        }
    }

    /// <summary>内容高度 = margin 1 + 行模型行数（projector 缺席 = 纯文本行数 — M8-1 行为）。</summary>
    public int MeasureHeight(int width) => 1 + LinesFor(width).Count;

    /// <summary>渲染：margin 行留空 → 首行 "  ⎿  " dim 前缀 → 各内容行从内容列起逐段写
    ///（段样式经 MessageRenderer.MapMarkdownStyle 映射 — 与 MarkdownMessageItem 段渲染同源；
    /// 续行 6 空格缩进对齐内容列）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        const string continuation = "      ";
        var row = frame.Y + 1;
        var first = true;
        foreach (var line in LinesFor(frame.Width)) {
            var prefix = first ? Figures.ToolResultIndent : continuation;
            screen.WriteText(row, frame.X, prefix, dim, frame.Right);
            if (line.Count > 0) {
                MessageRenderer.RenderMarkdownLine(screen, frame with { Y = row, Height = 1 }, theme, prefix.Length, line);
            }

            first = false;
            row++;
        }
    }

    /// <summary>行模型 — projector 缺席走纯文本（M8-1 现行为：\n 分行、超宽由 Screen 裁剪）；
    /// 在场走投影缓存链：脏刷新投影 → 稳定段布局（宽度缓存）+ gap 空行 + 尾纯文本行（宽度缓存）。</summary>
    private IReadOnlyList<IReadOnlyList<MarkdownSegment>> LinesFor(int width) {
        if (_projector is null) {
            return PlainLines();
        }

        RefreshProjection();
        if (_builtLines is null || _builtLinesWidth != width) {
            _builtLines = BuildProjectedLines(width);
            _builtLinesWidth = width;
        }

        return _builtLines;
    }

    /// <summary>纯文本行模型（M8-1 行为 — 空缓冲 1 空行；段化统一渲染路径，默认样式等价裸文本）。</summary>
    private IReadOnlyList<IReadOnlyList<MarkdownSegment>> PlainLines()
    {
        var lines = _buffer.Length == 0 ? (string[])[string.Empty] : _buffer.ToString().Split('\n');
        return lines
            .Select(static line => line.Length == 0
                ? []
                : (IReadOnlyList<MarkdownSegment>)[new MarkdownSegment(line, MarkdownStyle.None)])
            .ToList();
    }

    /// <summary>脏刷新投影 — Append 后首次访问；投影变化连带失效稳定布局与行模型缓存。</summary>
    private void RefreshProjection() {
        if (!_projectionDirty) {
            return;
        }

        _projection = _projector!.Project(_buffer.ToString());
        _projectionDirty = false;
        _stableLines = null;
        _builtLines = null;
    }

    /// <summary>投影行模型 — 稳定段布局行 + 稳定/尾间 gap 空行（块间空行语义 — 落定替换视觉无跳变）
    /// + 尾纯文本行（未闭合围栏尾 Dim 样式）；空投影兜底 1 空行（高度对齐纯文本空缓冲形态）。</summary>
    private IReadOnlyList<IReadOnlyList<MarkdownSegment>> BuildProjectedLines(int width) {
        var projection = _projection!;
        var lines = new List<IReadOnlyList<MarkdownSegment>>(StableLinesFor(width));
        var tail = projection.Tail;
        if (projection.StableBlocks.Count > 0 && tail.Length > 0) {
            lines.Add([]);
        }

        if (tail.Length > 0) {
            var tailStyle = projection.TailOpenFence ? MarkdownStyle.Dim : MarkdownStyle.None;
            foreach (var line in tail.Split('\n')) {
                lines.Add(line.Length == 0 ? [] : [new MarkdownSegment(line, tailStyle)]);
            }
        }

        if (lines.Count == 0) {
            lines.Add([]);
        }

        return lines;
    }

    /// <summary>稳定段布局行（块模型 → MarkdownLayout 物理行 — 顶层块间空 1 行由布局层补；
    /// 投影刷新/宽度变化才重算 — 对齐 MarkdownMessageItem.LayoutFor 缓存模式，内容宽 = width - 6）。</summary>
    private IReadOnlyList<IReadOnlyList<MarkdownSegment>> StableLinesFor(int width) {
        if (_stableLines is not null && _stableLinesWidth == width) {
            return _stableLines;
        }

        _stableLines = MarkdownLayout.LayoutWithHighlights(
            new MarkdownDocument(_projection!.StableBlocks), Math.Max(0, width - ContentIndentCols), null).Lines;
        _stableLinesWidth = width;
        return _stableLines;
    }
}
