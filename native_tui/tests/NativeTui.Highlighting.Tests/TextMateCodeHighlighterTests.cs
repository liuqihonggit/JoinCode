namespace NativeTui.Highlighting.Tests;

/// <summary>
/// TextMateCodeHighlighter 行为测试（M7-5）— 语言解析（id/别名/未支持降级 null）、
/// 跨行语法状态（块注释跨行着色）、色段区间映射（token → HighlightSpan）、
/// 幂等性（同块重复高亮结果一致 — 布局缓存重算场景）。
/// </summary>
public sealed class TextMateCodeHighlighterTests
{
    /// <summary>构造高亮器（dark 主题）。</summary>
    private static TextMateCodeHighlighter Create() => new(dark: true);

    [Theory]
    [InlineData("csharp")]
    [InlineData("cs")]
    [InlineData("bash")]
    [InlineData("shellscript")]
    [InlineData("json")]
    [InlineData("python")]
    [InlineData("ts")]
    public void Highlight_CommonLanguage_ProducesSpans(string language)
    {
        var highlighter = Create();

        var result = highlighter.Highlight(language, ["var x = 1; // note"]);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.NotEmpty(result[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-such-language")]
    public void Highlight_UnknownLanguage_ReturnsNull(string language)
    {
        var highlighter = Create();

        Assert.Null(highlighter.Highlight(language, ["anything"]));
    }

    [Fact]
    public void Highlight_StringLiteralGetsMonokaiYellow()
    {
        // Monokai 主题 string 色 = rgb(230,219,116)（探针实测输出同值）
        var highlighter = Create();

        var result = highlighter.Highlight("csharp", ["var s = \"abc\";"]);

        Assert.NotNull(result);
        var stringSpan = result![0].FirstOrDefault(span => span.R == 230 && span.G == 219 && span.B == 116);
        Assert.True(stringSpan.Length > 0, $"string span not found in [{string.Join(",", result[0].Select(s => $"{s.Start}+{s.Length}:{s.R},{s.G},{s.B}"))}]");
    }

    [Fact]
    public void Highlight_CommentTokenGetsMonokaiCommentColor()
    {
        // Monokai 主题 comment 色 = rgb(136,132,111)（探针实测输出同值）
        var highlighter = Create();

        var result = highlighter.Highlight("csharp", ["// whole line comment"]);

        Assert.NotNull(result);
        var commentSpans = result![0].Where(span => span.R == 136 && span.G == 132 && span.B == 111).ToList();
        Assert.NotEmpty(commentSpans);
        Assert.Equal(0, commentSpans[0].Start);
        Assert.Equal("// whole line comment".Length, commentSpans.Sum(span => span.Length));
    }

    [Fact]
    public void Highlight_BlockCommentSpansLines_StateCarriesOver()
    {
        // 跨行状态：/* 开注释 → 第二行仍在注释内（ruleStack 逐行推进）
        var highlighter = Create();

        var result = highlighter.Highlight("csharp", ["var a = 1; /* start", "still comment", "end */ var b = 2;"]);

        Assert.NotNull(result);
        var line2Comment = result![1].Where(span => span.R == 136 && span.G == 132 && span.B == 111).ToList();
        Assert.NotEmpty(line2Comment);
        Assert.Equal("still comment".Length, line2Comment.Sum(span => span.Length));
    }

    [Fact]
    public void Highlight_SameBlockTwice_IsIdempotent()
    {
        // 布局缓存失效重算场景 — 每次调用独立会话，同输入同输出
        var highlighter = Create();
        var lines = new[] { "var x = 1;", "/* c", "still */ var y = 2;" };

        var first = highlighter.Highlight("csharp", lines);
        var second = highlighter.Highlight("csharp", lines);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(
            first!.SelectMany(line => line).ToList(),
            second!.SelectMany(line => line).ToList());
    }

    [Fact]
    public void Highlight_EmptyLine_ProducesEmptySpanList()
    {
        var highlighter = Create();

        var result = highlighter.Highlight("csharp", ["var x = 1;", "", "var y = 2;"]);

        Assert.NotNull(result);
        Assert.Empty(result![1]);
    }

    [Fact]
    public void Highlight_SpanBoundsWithinLineLength()
    {
        var highlighter = Create();
        const string line = "var keyword = \"string value\"; // comment";

        var result = highlighter.Highlight("csharp", [line]);

        Assert.NotNull(result);
        Assert.All(result![0], span =>
        {
            Assert.InRange(span.Start, 0, line.Length);
            Assert.InRange(span.Start + span.Length, span.Start, line.Length);
        });
    }
}
