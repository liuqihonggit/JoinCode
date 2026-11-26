namespace NativeTui.Tests.Repl;

/// <summary>
/// transcript 页长行折行（TASK106 真机缺陷）：详情页是绝对定位写入，右缘即裁、没有终端软折行 —
/// 不预折的话系统提示词这类整段长文在页面上被切成断句（读不到句尾）。
/// 这里锁三件事：折行按显示宽度（CJK 双宽）、英文优先在空白断、快照投影确实按当前宽度折。
/// </summary>
public sealed class TranscriptWrapTests
{
    /// <summary>固定尺寸源（摆脱测试进程 Console 环境依赖）。</summary>
    private sealed class FixedSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Cols, int Rows) Current => (cols, rows);
    }

    [Fact]
    public void Wrap_ShortLine_Unchanged()
    {
        Assert.Equal(["hello"], TranscriptLines.Wrap("hello", 20));
    }

    [Fact]
    public void Wrap_CjkWideChars_RespectsDisplayWidth()
    {
        // 8 个 CJK 字 = 16 列；宽 8 → 每行 4 字
        var lines = TranscriptLines.Wrap("一二三四五六七八", 8).ToList();

        Assert.Equal(["一二三四", "五六七八"], lines);
        Assert.All(lines, line => Assert.True(RuneWidth.OfText(line) <= 8, line));
    }

    [Fact]
    public void Wrap_AsciiBreaksAtSpace()
    {
        var lines = TranscriptLines.Wrap("aaa bbb ccc", 8).ToList();

        Assert.Equal(["aaa bbb", "ccc"], lines);
    }

    [Fact]
    public void Wrap_LongToken_HardBreaks()
    {
        var lines = TranscriptLines.Wrap("abcdefghijklmno", 5).ToList();

        Assert.Equal(["abcde", "fghij", "klmno"], lines);
    }

    /// <summary>中文段落里的英文单词不被腰斩 — 断在中英边界（真机所见 "Co|mmonMark" 那类硬切）。</summary>
    [Fact]
    public void Wrap_KeepsAsciiWordIntact()
    {
        var lines = TranscriptLines.Wrap("中文 CommonMark 中文", 12).ToList();

        Assert.Equal(["中文", "CommonMark", "中文"], lines);
    }

    [Fact]
    public void Wrap_ZeroWidth_ReturnsLineAsIs()
    {
        Assert.Equal(["abc"], TranscriptLines.Wrap("abc", 0));
    }

    /// <summary>快照投影按当前终端宽度折行 — 任何一行都不得超出屏宽（真机右缘裁切回归）。</summary>
    [Fact]
    public void Snapshot_WrapsToCurrentWidth()
    {
        var longText = string.Concat(Enumerable.Repeat("系统提示词长段落内容", 12));
        var engine = new FakeReplEngine();
        var shell = new ReplShell(
            engine,
            new BannerItem("0.0.0", "m", "b", "C:\\"),
            new StringWriter(),
            seedMessages: [new UserMessageItem(longText)],
            sizeSource: new FixedSizeSource(40, 30));
        shell.SeedContent();

        var lines = shell.BuildTranscriptSnapshot().Lines;

        Assert.All(lines, line => Assert.True(RuneWidth.OfText(line) <= 40, $"超宽行: {line}"));
        Assert.True(lines.Count > 2, "长段落应被折成多行");
    }
}
