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

    [Fact]
    public void Tokenize_MultiWordKeyword_ContainsMatch()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Task.WhenAll", "flaky test" };
        var tokens = InputTokenizer.Tokenize("改用Task.WhenAll并行", dict);

        tokens.Should().Contain("Task.WhenAll");
    }

    [Fact]
    public void Tokenize_MultiWordKeyword_SpaceSeparated()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "flaky test" };
        var tokens = InputTokenizer.Tokenize("遇到flaky test怎么办", dict);

        tokens.Should().Contain("flaky test");
    }

    [Fact]
    public void Tokenize_RealWorld_FactInquiry()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "写一", "做一", "分析", "总结", "规划", "方案", "生成", "周报", "报告", "文档" };
        var tokens = InputTokenizer.Tokenize("帮我写一份周报", dict);

        tokens.Should().Contain("写一");
    }

    [Fact]
    public void Tokenize_RealWorld_UserDelegation()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "睡觉", "离开", "走了", "看着办", "晚安" };
        var tokens = InputTokenizer.Tokenize("我睡觉去了，后面看着办", dict);

        tokens.Should().Contain("睡觉");
        tokens.Should().Contain("看着办");
    }

    [Fact]
    public void Tokenize_RealWorld_PerformanceAudit()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GC压力", "性能优化", "Span", "Task.WhenAll", "AsParallel" };
        var tokens = InputTokenizer.Tokenize("这里GC压力很大，改用Task.WhenAll并行", dict);

        tokens.Should().Contain("GC压力");
        tokens.Should().Contain("Task.WhenAll");
    }

    [Fact]
    public void Tokenize_SubstringFalsePositive_Avoided()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "归纳" };
        var tokens = InputTokenizer.Tokenize("归纳整理这些问题", dict);

        tokens.Should().Contain("归纳");
    }

    [Fact]
    public void Tokenize_SubstringNotInDict_NotMatched()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "睡觉" };
        var tokens = InputTokenizer.Tokenize("了解这个问题", dict);

        tokens.Should().NotContain("睡觉");
    }

    [Fact]
    public void Tokenize_EnglishWordNotInDict_KeptWhole()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "优化" };
        var tokens = InputTokenizer.Tokenize("performance优化", dict);

        tokens.Should().Contain("performance");
        tokens.Should().Contain("优化");
    }

    [Fact]
    public void Tokenize_NumberKeptWhole()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "分钟后" };
        var tokens = InputTokenizer.Tokenize("3分钟后执行", dict);

        tokens.Should().Contain("3");
    }

    [Fact]
    public void Tokenize_ConsecutiveEnglishWords()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "auto" };
        var tokens = InputTokenizer.Tokenize("auto merge PR", dict);

        tokens.Should().Contain("auto");
        tokens.Should().Contain("merge");
        tokens.Should().Contain("PR");
    }

    [Fact]
    public void Tokenize_RepeatedCalls_SameDictionary_ReturnsConsistentResult()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "写一", "分析", "总结", "规划" };
        var input = "帮我写一份周报，然后分析数据";

        var tokens1 = InputTokenizer.Tokenize(input, dict);
        var tokens2 = InputTokenizer.Tokenize(input, dict);
        var tokens3 = InputTokenizer.Tokenize(input, dict);

        tokens1.Should().Equal(tokens2);
        tokens2.Should().Equal(tokens3);
    }

    [Fact]
    public void Tokenize_DifferentDictionaries_CacheIsolation()
    {
        var dict1 = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "分析" };
        var dict2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "睡觉" };

        var tokens1 = InputTokenizer.Tokenize("帮我分析数据", dict1);
        var tokens2 = InputTokenizer.Tokenize("我去睡觉了", dict2);

        tokens1.Should().Contain("分析");
        tokens1.Should().NotContain("睡觉");
        tokens2.Should().Contain("睡觉");
        tokens2.Should().NotContain("分析");
    }

    [Fact]
    public void Tokenize_MultiWordKeyword_CacheHit_PreservesAcAutomaton()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "flaky test", "keep going", "go hard" };
        var input = "遇到flaky test怎么办，需要keep going，go hard";

        var tokens1 = InputTokenizer.Tokenize(input, dict);
        var tokens2 = InputTokenizer.Tokenize(input, dict);

        tokens1.Should().Contain("flaky test");
        tokens1.Should().Contain("keep going");
        tokens1.Should().Contain("go hard");
        tokens2.Should().Equal(tokens1);
    }

    [Fact]
    public void Tokenize_MixedDict_RepeatedCalls_MultiWordAndFmmBothCorrect()
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "GC压力", "Task.WhenAll", "死锁", "flaky test", "性能优化", "Span" };
        var input = "这里GC压力很大，改用Task.WhenAll并行，避免死锁和flaky test";

        var tokens1 = InputTokenizer.Tokenize(input, dict);
        var tokens2 = InputTokenizer.Tokenize(input, dict);

        tokens1.Should().Contain("GC压力");
        tokens1.Should().Contain("Task.WhenAll");
        tokens1.Should().Contain("死锁");
        tokens1.Should().Contain("flaky test");
        tokens2.Should().Equal(tokens1);
    }
}
