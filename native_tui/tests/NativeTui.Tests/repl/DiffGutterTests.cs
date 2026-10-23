namespace NativeTui.Tests.Repl;

/// <summary>
/// diff 行号 gutter + 语法高亮测试（TASK066 板块 F 基建 F1）：
/// hunk 头解析（"@@ -1,3 +1,4 @@" 标准 / "@@ -9 +9 @@" 单行省略 / "@@ -0,0 +1,1 @@" 0 基 / 畸形 false）/
/// 行号双源（DiffLine 行号字段引擎真值优先 / 旧 JSON 无字段降级 hunk 头流推进 —
/// 基准 color-diff/index.ts ColorDiff.render entries L877-895 算法：Added→new++、Removed→old++、
/// Context→当前 new 双侧推进）/ gutter 渲染 golden（增/删/上下文 — 单列右对齐 " N " + marker 保留）/
/// 满行背景（added/removed 行 DiffAdded/DiffRemoved 底色满宽 — 基准 wrapText pad solid 红绿条）+
/// gutter decoration 前景（DiffAddedWord/DiffRemovedWord）/ 语法着色（span 段前景 RGB + 行背景叠加 —
/// 基准 applyBackground 保背景换前景语义；Removed 行恒不高亮）/ digits 对齐（多位行号右对齐）/
/// 畸形 hunk 头降级无 gutter 现形态 / 语言检测（summary 路径扩展名）/ ToolResultItem 链路集成
/// （构造期预计算 + 高亮器注入 + 无高亮器降级）。
/// </summary>
public sealed class DiffGutterTests
{
    /// <summary>构造 diff 行（无行号字段 — 旧引擎 JSON 降级路径）。</summary>
    private static DiffLine Line(DiffLineKind kind, string content) => new() { Kind = kind, Content = content };

    /// <summary>构造 hunk（header + 行可变，行无行号字段 — 流推进路径）。</summary>
    private static DiffHunk Hunk(string header, params DiffLine[] lines) => new() { Header = header, Lines = lines };

    /// <summary>stub 高亮器 — 只接受指定语言，每行返回同一色段（测试简化）；其他语言 null（不支持降级）。</summary>
    private sealed class StubHighlighter(string acceptedLanguage, IReadOnlyList<HighlightSpan> spans) : ICodeHighlighter
    {
        public IReadOnlyList<IReadOnlyList<HighlightSpan>>? Highlight(string language, IReadOnlyList<string> lines) =>
            string.Equals(language, acceptedLanguage, StringComparison.OrdinalIgnoreCase)
                ? lines.Select(_ => spans).ToArray()
                : null;
    }

    [Fact]
    public void TryParseHunkHeader_StandardAndSingleLineAndZeroBasedForms()
    {
        // "@@ -1,3 +1,4 @@"（标准）→ 起始行号与行数
        Assert.True(DiffGutter.TryParseHunkHeader("@@ -1,3 +1,4 @@", out var oldStart, out var oldLines, out var newStart, out var newLines));
        Assert.Equal(1, oldStart);
        Assert.Equal(3, oldLines);
        Assert.Equal(1, newStart);
        Assert.Equal(4, newLines);

        // "@@ -9 +9 @@"（省略 count）→ count = 1（git 单行语义）
        Assert.True(DiffGutter.TryParseHunkHeader("@@ -9 +9 @@", out oldStart, out oldLines, out newStart, out newLines));
        Assert.Equal(9, oldStart);
        Assert.Equal(1, oldLines);
        Assert.Equal(9, newStart);
        Assert.Equal(1, newLines);

        // "@@ -0,0 +1,1 @@"（纯插入 0 基对齐 jsdiff）
        Assert.True(DiffGutter.TryParseHunkHeader("@@ -0,0 +1,1 @@", out oldStart, out oldLines, out newStart, out newLines));
        Assert.Equal(0, oldStart);
        Assert.Equal(0, oldLines);
        Assert.Equal(1, newStart);
        Assert.Equal(1, newLines);
    }

    [Fact]
    public void TryParseHunkHeader_MalformedReturnsFalse()
    {
        Assert.False(DiffGutter.TryParseHunkHeader(string.Empty, out _, out _, out _, out _));
        Assert.False(DiffGutter.TryParseHunkHeader("not a header", out _, out _, out _, out _));
        Assert.False(DiffGutter.TryParseHunkHeader("@@ x +1 @@", out _, out _, out _, out _));
        Assert.False(DiffGutter.TryParseHunkHeader("@@ -1,3", out _, out _, out _, out _));
    }

    [Fact]
    public void AssignLineNumbers_FieldValuesTakePrecedence()
    {
        // 引擎真值路径：全行行号字段在位 → 按行种取侧直读（Added→New / Removed→Old / Context→New）
        var hunk = Hunk("@@ -10,3 +20,4 @@",
            new DiffLine { Kind = DiffLineKind.Removed, Content = "r", OldLineNumber = 10 },
            new DiffLine { Kind = DiffLineKind.Added, Content = "a", NewLineNumber = 20 },
            new DiffLine { Kind = DiffLineKind.Context, Content = "c", OldLineNumber = 11, NewLineNumber = 21 });

        var lineNumbers = DiffGutter.AssignLineNumbers(hunk);

        Assert.Equal(new int?[] { 10, 20, 21 }, lineNumbers);
    }

    [Fact]
    public void AssignLineNumbers_HeaderStreamFallback()
    {
        // 降级路径（旧引擎 JSON 无行号字段）：hunk 头流推进 —
        // Context→当前 newLine 且双侧推进 / Removed→oldLine++ / Added→newLine++（基准 entries 算法）
        var hunk = Hunk("@@ -10,3 +20,4 @@",
            Line(DiffLineKind.Context, "c1"),
            Line(DiffLineKind.Removed, "r1"),
            Line(DiffLineKind.Added, "a1"),
            Line(DiffLineKind.Added, "a2"));

        var lineNumbers = DiffGutter.AssignLineNumbers(hunk);

        // Context=20（old→11/new→21）；Removed=11；Added=21；Added=22
        Assert.Equal(new int?[] { 20, 11, 21, 22 }, lineNumbers);
    }

    [Fact]
    public void AssignLineNumbers_PartialFieldsFallBackToStream()
    {
        // 混合残缺（首行有值、次行缺失）→ 整 hunk 统一走流推进（混合取值会错位 — 全有或全无；
        // 单行有值=全有形态直读真值，两行混合才触发回退）
        var hunk = Hunk("@@ -1 +1 @@",
            new DiffLine { Kind = DiffLineKind.Added, Content = "a", NewLineNumber = 99 },
            new DiffLine { Kind = DiffLineKind.Context, Content = "b" });

        // 流推进：Added→newLine=1；Context→newLine=2
        Assert.Equal(new int?[] { 1, 2 }, DiffGutter.AssignLineNumbers(hunk));
    }

    [Fact]
    public void AssignLineNumbers_MalformedHeaderYieldsAllNull()
    {
        var hunk = Hunk("not a header", Line(DiffLineKind.Added, "a"));

        Assert.Equal(new int?[] { null }, DiffGutter.AssignLineNumbers(hunk));
    }

    [Fact]
    public void GutterDigits_StreamOverrunAndHeaderEndTakeMax()
    {
        // 流推进实际行号越过 header 声明终点（demo 数据不自洽防御）：12 行 context 流推 1..12 → max 12（2 位）
        var overrun = Hunk("@@ -1,3 +1,4 @@", Enumerable.Range(0, 12).Select(_ => Line(DiffLineKind.Context, "x")).ToArray());
        // 畸形 hunk 头 + 无行号字段 → 0（降级无 gutter 列）
        var malformed = Hunk("not a header", Line(DiffLineKind.Added, "a"));
        // 行字段真值（max 321 → 3 位）
        var fromFields = Hunk("@@ -1 +1 @@",
            new DiffLine { Kind = DiffLineKind.Removed, Content = "r", OldLineNumber = 321 });

        Assert.Equal(2, DiffGutter.GutterDigits([overrun]));
        Assert.Equal(0, DiffGutter.GutterDigits([malformed]));
        Assert.Equal(0, DiffGutter.GutterDigits([]));
        Assert.Equal(3, DiffGutter.GutterDigits([fromFields]));
    }

    [Fact]
    public void DetectLanguage_ExtractsExtensionFromSummary()
    {
        Assert.Equal("cs", DiffGutter.DetectLanguage("Edited a.cs"));
        Assert.Equal("cs", DiffGutter.DetectLanguage("Updated demo/simulated.cs (simulated)"));
        Assert.Equal("py", DiffGutter.DetectLanguage("Read 10 lines from src/main.py"));
        Assert.Null(DiffGutter.DetectLanguage("Read 10 lines"));
        Assert.Null(DiffGutter.DetectLanguage("2 files"));
        Assert.Null(DiffGutter.DetectLanguage(string.Empty));
    }

    [Fact]
    public void HighlightHunkLines_RemovedLinesStayNullAndNewSideHighlighted()
    {
        // new 侧行（Context+Added 按序）高亮、Removed 行恒 null — 基准 marker==='-' 不高亮策略
        var spans = new HighlightSpan[] { new(0, 2, 10, 20, 30) };
        var hunk = Hunk("@@ -1 +1 @@",
            Line(DiffLineKind.Removed, "drop"),
            Line(DiffLineKind.Added, "add"),
            Line(DiffLineKind.Context, "keep"));

        var result = DiffGutter.HighlightHunkLines(hunk, new StubHighlighter("cs", spans), "cs");

        Assert.Null(result[0]);
        Assert.Same(spans, result[1]);
        Assert.Same(spans, result[2]);
    }

    [Fact]
    public void HighlightHunkLines_UnsupportedLanguageAllNull()
    {
        var hunk = Hunk("@@ -1 +1 @@", Line(DiffLineKind.Added, "add"));

        var result = DiffGutter.HighlightHunkLines(hunk, new StubHighlighter("cs", [new HighlightSpan(0, 1, 1, 2, 3)]), "py");

        Assert.Single(result);
        Assert.Null(result[0]);
    }

    [Fact]
    public void Render_GutterGoldenAddedRemovedContext()
    {
        // 单列 gutter golden：" N " 右对齐 + marker 保留（基准 addLineNumber + addMarker 行序）：
        // removed = "     1 -old" / added = "     1 +new" / context = "     1  keep"
        using var screen = new Screen(cols: 40, rows: 3);
        var rows = new[]
        {
            Line(DiffLineKind.Removed, "old"),
            Line(DiffLineKind.Added, "new"),
            Line(DiffLineKind.Context, "keep"),
        };

        for (var row = 0; row < rows.Length; row++)
        {
            MessageRenderer.RenderDiffLine(screen, new Rect(0, row, 40, 1), Theme.Dark, rows[row], lineNumber: 1, gutterDigits: 1, syntaxSpans: null);
        }

        // 满行背景填充（基准 Fallback 红绿条 pad 到行宽）— golden 断言 TrimEnd 对齐项目惯例
        Assert.Equal("     1 -old", RowText.Of(screen, 0, 0, 12).TrimEnd());
        Assert.Equal("     1 +new", RowText.Of(screen, 1, 0, 12).TrimEnd());
        Assert.Equal("     1  keep", RowText.Of(screen, 2, 0, 13).TrimEnd());
    }

    [Fact]
    public void Render_AddedLineBackgroundFillAndDecorationForeground()
    {
        // added 行：满行背景 DiffAdded（基准 solid 红绿条）+ gutter/marker 前景 DiffAddedWord（decoration）
        // + 正文前景 theme.Text（基准 defaultStyle fg=theme.foreground）
        using var screen = new Screen(cols: 40, rows: 1);

        MessageRenderer.RenderDiffLine(screen, new Rect(0, 0, 40, 1), Theme.Dark, Line(DiffLineKind.Added, "new"), 1, 1, null);

        var fill = screen.Styles.GetOrAdd(new TextStyle { Background = Theme.Dark.DiffAdded });
        var decoration = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.DiffAddedWord, Background = Theme.Dark.DiffAdded });
        var body = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text, Background = Theme.Dark.DiffAdded });

        Assert.Equal(new Rune('1'), screen[0, 5].Value);
        Assert.Equal(decoration, screen[0, 4].StyleId);
        Assert.Equal(decoration, screen[0, 5].StyleId);
        Assert.Equal(decoration, screen[0, 7].StyleId);
        Assert.Equal(body, screen[0, 8].StyleId);
        Assert.Equal(body, screen[0, 10].StyleId);
        Assert.Equal(fill, screen[0, 39].StyleId);
    }

    [Fact]
    public void Render_RemovedLineBackgroundFillAndDecorationForeground()
    {
        // removed 行：背景 DiffRemoved + gutter/marker DiffRemovedWord + 正文 theme.Text（无语法 — 基准策略）
        using var screen = new Screen(cols: 40, rows: 1);

        MessageRenderer.RenderDiffLine(screen, new Rect(0, 0, 40, 1), Theme.Dark, Line(DiffLineKind.Removed, "old"), 1, 1, null);

        var fill = screen.Styles.GetOrAdd(new TextStyle { Background = Theme.Dark.DiffRemoved });
        var decoration = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.DiffRemovedWord, Background = Theme.Dark.DiffRemoved });
        var body = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text, Background = Theme.Dark.DiffRemoved });

        Assert.Equal(new Rune('-'), screen[0, 7].Value);
        Assert.Equal(decoration, screen[0, 7].StyleId);
        Assert.Equal(body, screen[0, 8].StyleId);
        Assert.Equal(fill, screen[0, 39].StyleId);
    }

    [Fact]
    public void Render_ContextLineDimmedIncludingGutter()
    {
        // context 行整行 dim（含 gutter — 基准 context 无背景 dimColor；现形态保留）
        using var screen = new Screen(cols: 40, rows: 1);

        MessageRenderer.RenderDiffLine(screen, new Rect(0, 0, 40, 1), Theme.Dark, Line(DiffLineKind.Context, "keep"), 1, 1, null);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[0, 4].StyleId);
        Assert.Equal(dim, screen[0, 5].StyleId);
        Assert.Equal(dim, screen[0, 8].StyleId);
    }

    [Fact]
    public void Render_SyntaxSpansOverrideForegroundKeepBackground()
    {
        // 语法叠加（基准 applyBackground 镜像）：段前景 = token RGB、背景保持 DiffAdded；
        // 段间隙默认前景 theme.Text + 同背景 — content "ab cd" + span(3,2) 覆盖 "cd"
        using var screen = new Screen(cols: 40, rows: 1);
        var spans = new HighlightSpan[] { new(3, 2, 10, 20, 30) };

        MessageRenderer.RenderDiffLine(screen, new Rect(0, 0, 40, 1), Theme.Dark, Line(DiffLineKind.Added, "ab cd"), 1, 1, spans);

        var body = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text, Background = Theme.Dark.DiffAdded });
        var syntax = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(10, 20, 30), Background = Theme.Dark.DiffAdded });

        Assert.Equal(new Rune('a'), screen[0, 8].Value);
        Assert.Equal(body, screen[0, 8].StyleId);
        Assert.Equal(body, screen[0, 10].StyleId);
        Assert.Equal(new Rune('c'), screen[0, 11].Value);
        Assert.Equal(syntax, screen[0, 11].StyleId);
        Assert.Equal(syntax, screen[0, 12].StyleId);
    }

    [Fact]
    public void Render_NullLineNumberGutterBlankOccupied()
    {
        // 行号 null 但 digits>0（畸形行混入有行号 patch）→ gutter 空占位（基准 wrap 续行对齐语义）
        using var screen = new Screen(cols: 40, rows: 1);

        MessageRenderer.RenderDiffLine(screen, new Rect(0, 0, 40, 1), Theme.Dark, Line(DiffLineKind.Added, "new"), null, 1, null);

        // gutter 3 列空占位 → marker '+' 仍在 col7（与有行号行对齐）
        Assert.Equal(new Rune('+'), screen[0, 7].Value);
        Assert.Equal("       +new", RowText.Of(screen, 0, 0, 11));
    }

    [Fact]
    public void Render_ZeroDigitsLegacyFormWithoutGutter()
    {
        // digits=0（整块无行号降级）→ 无 gutter 列现形态 "    +new"（背景增强保留）
        using var screen = new Screen(cols: 40, rows: 1);

        MessageRenderer.RenderDiffLine(screen, new Rect(0, 0, 40, 1), Theme.Dark, Line(DiffLineKind.Added, "new"), null, 0, null);

        Assert.Equal("    +new", RowText.Of(screen, 0, 0, 8));
        var fill = screen.Styles.GetOrAdd(new TextStyle { Background = Theme.Dark.DiffAdded });
        Assert.Equal(fill, screen[0, 39].StyleId);
    }

    [Fact]
    public void Render_MultiDigitGutterRightAlignsMarkerColumn()
    {
        // digits=2：行号 2 与 12 的 marker '+' 都在 col8（右对齐 — 全 patch 统一宽对齐语义）
        using var screen = new Screen(cols: 40, rows: 2);

        MessageRenderer.RenderDiffLine(screen, new Rect(0, 0, 40, 1), Theme.Dark, Line(DiffLineKind.Added, "a"), 2, 2, null);
        MessageRenderer.RenderDiffLine(screen, new Rect(0, 1, 40, 1), Theme.Dark, Line(DiffLineKind.Added, "b"), 12, 2, null);

        Assert.Equal(new Rune('+'), screen[0, 8].Value);
        Assert.Equal(new Rune('+'), screen[1, 8].Value);
        Assert.Equal("      2 +a", RowText.Of(screen, 0, 0, 10));
        Assert.Equal("     12 +b", RowText.Of(screen, 1, 0, 10));
    }

    [Fact]
    public void ToolResultItem_WiresHighlighterAndGutterThroughRender()
    {
        // 链路集成：构造期预计算（行号流推进 + summary "Edited a.cs" → 语言 "cs" → stub 高亮）
        // → 渲染 row3 = "     1 +var s = \"x\";" 且 span(4,3) 段前景 RGB + 背景 DiffAdded
        using var screen = new Screen(cols: 40, rows: 4);
        var patch = new[] { Hunk("@@ -1,3 +1,4 @@", Line(DiffLineKind.Added, "var s = \"x\";")) };
        var spans = new HighlightSpan[] { new(4, 3, 10, 20, 30) };
        var item = new ToolResultItem("Edited a.cs", patch, null, 0, codeHighlighter: new StubHighlighter("cs", spans));

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal("    @@ -1,3 +1,4 @@", RowText.Of(screen, 2, 0, 19));
        Assert.Equal("     1 +var s = \"x\";", RowText.Of(screen, 3, 0, 20));
        var syntax = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(10, 20, 30), Background = Theme.Dark.DiffAdded });
        // content col8 起：'v'0 'a'1 'r'2 ' '3 — span(4,3) = content[4..7] "s =" → col12..14 前景 RGB
        Assert.Equal(syntax, screen[3, 12].StyleId);
        Assert.Equal(syntax, screen[3, 14].StyleId);
    }

    [Fact]
    public void ToolResultItem_WithoutHighlighterRendersPlainDiffColors()
    {
        // 无高亮器 → 正文纯 bodyStyle（theme.Text 前景 + 行背景）— 降级不阻塞渲染
        using var screen = new Screen(cols: 40, rows: 4);
        var patch = new[] { Hunk("@@ -1,3 +1,4 @@", Line(DiffLineKind.Added, "var s = \"x\";")) };
        var item = new ToolResultItem("Edited a.cs", patch, null, 0);

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        var body = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text, Background = Theme.Dark.DiffAdded });
        Assert.Equal(body, screen[3, 8].StyleId);
        Assert.Equal(body, screen[3, 12].StyleId);
    }

    [Fact]
    public void ToolResultItem_NoLanguageInSummarySkipsHighlight()
    {
        // summary 无路径（"Read 10 lines"）→ 语言检测 null → stub 不触发、正文 bodyStyle
        using var screen = new Screen(cols: 40, rows: 4);
        var patch = new[] { Hunk("@@ -1,3 +1,4 @@", Line(DiffLineKind.Added, "text")) };
        var item = new ToolResultItem("Read 10 lines", patch, null, 0, codeHighlighter: new StubHighlighter("cs", [new HighlightSpan(0, 2, 1, 2, 3)]));

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        var body = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text, Background = Theme.Dark.DiffAdded });
        Assert.Equal(body, screen[3, 8].StyleId);
    }

    [Fact]
    public void ToolResultItem_MalformedHeaderRendersLegacyForm()
    {
        // 畸形 hunk 头 → digits 0 → 无 gutter 现形态文本（"    +new"）
        using var screen = new Screen(cols: 40, rows: 4);
        var patch = new[] { Hunk("not a header", Line(DiffLineKind.Added, "new")) };
        var item = new ToolResultItem("Edited a.cs", patch, null, 0);

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal("    +new", RowText.Of(screen, 3, 0, 8));
    }
}
