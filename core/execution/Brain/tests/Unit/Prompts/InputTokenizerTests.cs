using Core.Prompts.Utils;
using FluentAssertions;

namespace Core.Tests.Prompts;

public class InputTokenizerTests
{
    [Fact]
    public void Tokenize_EmptyInput_ReturnsEmpty()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "睡觉" };
        InputTokenizer.Tokenize("", dict).Should().BeEmpty();
        InputTokenizer.Tokenize("   ", dict).Should().BeEmpty();
        InputTokenizer.Tokenize(null!, dict).Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_EmptyDictionary_ReturnsCoarseSegments()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = InputTokenizer.Tokenize("帮我写一份周报", dict);

        tokens.Should().ContainSingle("帮我写一份周报");
    }

    [Fact]
    public void Tokenize_ChineseWithPunctuation_SplitsByPunctuation()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "写一", "分析" };
        var tokens = InputTokenizer.Tokenize("帮我写一份周报，然后分析数据", dict);

        tokens.Should().Contain("写一");
        tokens.Should().Contain("分析");
    }

    [Fact]
    public void Tokenize_FmmMatchesLongestFirst()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "批量", "批量替换" };
        var tokens = InputTokenizer.Tokenize("批量替换命名空间", dict);

        tokens.Should().Contain("批量替换");
        tokens.Should().NotContain("批量");
    }

    [Fact]
    public void Tokenize_SingleCharFallback()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "睡觉" };
        var tokens = InputTokenizer.Tokenize("我睡觉了", dict);

        tokens.Should().Contain("睡觉");
        tokens.Should().Contain("我");
        tokens.Should().Contain("了");
    }

    [Fact]
    public void Tokenize_MixedChineseEnglish()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "chrome", "浏览器" };
        var tokens = InputTokenizer.Tokenize("打开chrome浏览器", dict);

        tokens.Should().Contain("chrome");
        tokens.Should().Contain("浏览器");
    }

    [Fact]
    public void Tokenize_EnglishWithSpaces()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "keep", "going" };
        var tokens = InputTokenizer.Tokenize("keep going please", dict);

        tokens.Should().Contain("keep");
        tokens.Should().Contain("going");
    }

    [Fact]
    public void Tokenize_MultiplePunctuationSeparators()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "死锁", "GC" };
        var tokens = InputTokenizer.Tokenize("GC压力！导致死锁？", dict);

        tokens.Should().Contain("GC");
        tokens.Should().Contain("死锁");
    }

    [Fact]
    public void Tokenize_KeywordAtSentenceEnd()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "睡觉" };
        var tokens = InputTokenizer.Tokenize("我先睡觉", dict);

        tokens.Should().Contain("睡觉");
    }

    [Fact]
    public void Tokenize_NoMatchInDictionary_SingleChars()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "xyz" };
        var tokens = InputTokenizer.Tokenize("你好世界", dict);

        tokens.Should().Equal("你", "好", "世", "界");
    }
}
