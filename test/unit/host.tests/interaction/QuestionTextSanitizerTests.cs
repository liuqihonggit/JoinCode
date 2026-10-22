namespace Host.Tests.Interaction;

/// <summary>
/// 问题文本规整测试 — AI 生成的 question/options/description 输入清理与宽度换行：
/// ①清理：\r\n 归一 \n、裸 \r 归一 \n（Windows/混合行尾）、剥离其余 C0 控制字符（\t 保留转空格、ESC 剥除防终端解释）；
/// ②换行：按显示宽度词边界折行（CJK 宽字符按 2 列计）、悬挂缩进对齐、宽度钳制（超宽词硬切）。
/// </summary>
public sealed class QuestionTextSanitizerTests {
    [Theory]
    [InlineData("a\r\nb", "a\nb")]          // CRLF → LF
    [InlineData("a\rb", "a\nb")]             // 裸 CR → LF（AI 混入）
    [InlineData("a\r\r\nb", "a\nb")]         // \r\r\n 病态混合（question 工具实测）→ 单换行
    [InlineData("a\n\rb", "a\nb")]           // \n\r 混合 → 单换行
    [InlineData("a\x1b[31mb", "a[31mb")]     // ESC 剥除（防终端解释 ANSI）—— 方括号字面保留
    [InlineData("a\u0000b", "ab")]           // NUL 剥除（\u 定长转义 — \x00 变长陷阱：\x00b 会被吃成单字符 0x0B）
    [InlineData("a\tb", "a b")]              // TAB → 单空格（对齐语义稳定）
    [InlineData("正常文本", "正常文本")]       // 干净文本零变化
    [InlineData("", "")]                     // 空串零变化
    public void Sanitize_Normalizes_And_Strips(string input, string expected) {
        Assert.Equal(expected, QuestionTextSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_CrLfRuns_Collapse() {
        // 连续 \r 段（AI 生成的超长换行根因）→ 单 \n
        Assert.Equal("行1\n行2", QuestionTextSanitizer.Sanitize("行1\r\r\r\r\n行2"));
    }

    [Fact]
    public void Wrap_ShortLine_Unchanged() {
        Assert.Equal(["短文本"], QuestionTextSanitizer.Wrap("短文本", width: 20, indent: 0));
    }

    [Fact]
    public void Wrap_Words_Break_At_Width() {
        // 10 ASCII 词，宽 11（容纳 2 词/行）→ 词边界折行
        var text = "one two three four five six seven eight nine ten";
        var lines = QuestionTextSanitizer.Wrap(text, width: 11, indent: 0);
        Assert.Equal("one two", lines[0]);
        Assert.Equal("three four", lines[1]);
        Assert.Equal("five six", lines[2]);
    }

    [Fact]
    public void Wrap_Cjk_Counted_As_Two_Columns() {
        // 8 个汉字 = 16 列，宽 8 → 每行 4 字
        var lines = QuestionTextSanitizer.Wrap("一二三四五六七八", width: 8, indent: 0);
        Assert.Equal(["一二三四", "五六七八"], lines);
    }

    [Fact]
    public void Wrap_Indent_Hangs_Aligned() {
        // 悬挂缩进：续行前留 indent 空格，可用宽度 = width - indent
        var lines2 = QuestionTextSanitizer.Wrap("aaaa bbbb cccc", width: 9, indent: 5);
        Assert.Equal("aaaa", lines2[0]);
        Assert.Equal("     bbbb", lines2[1]);  // 5 空格缩进 + 词
    }

    [Fact]
    public void Wrap_OverlongWord_Hard_Splits() {
        // 超宽词（无空格长串）按可用宽度硬切防单行超屏
        var lines = QuestionTextSanitizer.Wrap("abcdefghij", width: 4, indent: 0);
        Assert.Equal(["abcd", "efgh", "ij"], lines);
    }

    [Fact]
    public void Wrap_MultiParagraph_Kept() {
        // 段落（\n）保留 — 每段独立折行
        var lines = QuestionTextSanitizer.Wrap("one two\nthree four", width: 8, indent: 0);
        Assert.Equal(["one two", "three", "four"], lines);
    }

    [Fact]
    public void Wrap_ZeroWidth_Returns_Unfolded_Single() {
        // 不可用宽度防退化 — 原文单行返回（信息不丢）
        Assert.Equal(["any"], QuestionTextSanitizer.Wrap("any", width: 0, indent: 0));
    }

    [Fact]
    public void SanitizeAndWrap_Pipeline() {
        // 组合：清理后折行 — AI 实测形态（\r 混入 + 长中文）
        var sanitized = QuestionTextSanitizer.Sanitize("问题一\r\r\r回答一");
        var lines = QuestionTextSanitizer.Wrap(sanitized, width: 6, indent: 0);
        Assert.Equal(["问题一", "回答一"], lines);
    }
}
