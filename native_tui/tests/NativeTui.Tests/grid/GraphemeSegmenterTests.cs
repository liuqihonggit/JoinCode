namespace NativeTui.Tests.ScreenTests;

/// <summary>
/// Grapheme 分段测试 — UAX #29 Extended Grapheme Cluster 核心（对齐基准 ink/stringWidth.ts
/// grapheme 分段前提）：ZWJ emoji 链整块、RI 旗帜对合并、组合符/变体选择器附加、代理对完整、CRLF 合并。
/// 消费方：RuneWidth.WidthOfGrapheme（grapheme 整块计宽）与 InputBuffer grapheme 光标模型（DSG044 L2）。
/// </summary>
public sealed class GraphemeSegmenterTests
{
    /// <summary>枚举整段文本的全部 grapheme 范围。</summary>
    private static List<string> SplitAll(string text)
    {
        var clusters = new List<string>();
        var start = 0;
        while (GraphemeSegmenter.TryNext(text, start, out var end))
        {
            clusters.Add(text[start..end]);
            start = end;
        }

        return clusters;
    }

    /// <summary>ZWJ emoji 链（家庭组合 👨‍👩‍👧 = 3 emoji + 2 ZWJ）整块一个 cluster。</summary>
    [Fact]
    public void ZwjEmojiChain_SingleCluster()
    {
        var clusters = SplitAll("a\U0001F468\u200D\U0001F469\u200D\U0001F467b");
        Assert.Equal(["a", "\U0001F468\u200D\U0001F469\u200D\U0001F467", "b"], clusters);
    }

    /// <summary>RI 旗帜对（🇨🇳 = 2 个 Regional Indicator）合并为一个 cluster。</summary>
    [Fact]
    public void RegionalIndicatorPair_SingleCluster()
    {
        var clusters = SplitAll("\U0001F1E8\U0001F1F3x");
        Assert.Equal(["\U0001F1E8\U0001F1F3", "x"], clusters);
    }

    /// <summary>连续两个旗帜对各自成 cluster（RI 奇偶配对 — 第二对起断开）。</summary>
    [Fact]
    public void TwoFlagPairs_TwoClusters()
    {
        var clusters = SplitAll("\U0001F1E8\U0001F1F3\U0001F1FA\U0001F1F8");
        Assert.Equal(2, clusters.Count);
    }

    /// <summary>组合符附加（e + U+0301 组合尖调 = é 单 cluster）。</summary>
    [Fact]
    public void CombiningMark_AttachesToBase()
    {
        var clusters = SplitAll("e\u0301");
        Assert.Single(clusters);
    }

    /// <summary>变体选择器附加（emoji + VS16 单 cluster）。</summary>
    [Fact]
    public void VariationSelector_AttachesToBase()
    {
        var clusters = SplitAll("\u270C\uFE0F");
        Assert.Single(clusters);
    }

    /// <summary>代理对完整（ 😀 单 rune 不拆半）。</summary>
    [Fact]
    public void SurrogatePair_NotSplit()
    {
        var clusters = SplitAll("a\U0001F600b");
        Assert.Equal(["a", "\U0001F600", "b"], clusters);
    }

    /// <summary>CRLF 合并（Windows 换行单 cluster）。</summary>
    [Fact]
    public void CrLf_SingleCluster()
    {
        var clusters = SplitAll("a\r\nb");
        Assert.Equal(["a", "\r\n", "b"], clusters);
    }

    /// <summary>普通 ASCII/CJK 逐字符。</summary>
    [Fact]
    public void PlainChars_OneClusterEach()
    {
        Assert.Equal(["你", "好"], SplitAll("你好"));
        Assert.Equal(["a", "b"], SplitAll("ab"));
    }

    /// <summary>start 越界返回 false。</summary>
    [Fact]
    public void StartBeyondLength_ReturnsFalse()
    {
        Assert.False(GraphemeSegmenter.TryNext("abc", 3, out _));
    }
}
